using System.IO;
using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;
using CNCSS.Vis;

namespace CNCSS.Machine.Configuration
{
    /// <summary>Imports a multi-body STEP/IGES assembly into a machine profile (base, table, spindle).</summary>
    public static class MachineProfileAssemblyImporter
    {
        private static readonly (MachineComponentRole Role, MachineNodeKind Kind, string NodeId)[] DefaultOrder =
        [
            (MachineComponentRole.Base, MachineNodeKind.Base, MachineNodeIds.Base),
            (MachineComponentRole.Table, MachineNodeKind.Table, MachineNodeIds.Table),
            (MachineComponentRole.Spindle, MachineNodeKind.Spindle, MachineNodeIds.Spindle)
        ];

        /// <summary>
        /// Tessellates <paramref name="stepPath"/> and assigns the first three solids to base, table, spindle (in file order).
        /// </summary>
        public static void ImportStepAssembly(
            MachineProfileStore store,
            MachineDefinition definition,
            string stepPath,
            StlMeshLoader stlLoader)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentNullException.ThrowIfNull(stlLoader);
            if (string.IsNullOrWhiteSpace(stepPath) || !File.Exists(stepPath))
            {
                throw new FileNotFoundException("Файл сборки станка не найден.", stepPath);
            }

            string profileId = definition.ProfileId;
            Directory.CreateDirectory(store.GetProfileDirectory(profileId));

            IReadOnlyList<CadComponentStlExport> exports = OcctCadThreadRunner
                .RunAsync(() => OcctCadModelConverter.ExportComponentsToStl(stepPath, CadMeshQualitySettings.Standard))
                .GetAwaiter()
                .GetResult();

            if (exports.Count < 3)
            {
                throw new InvalidOperationException(
                    $"В сборке ожидается минимум 3 тела (основание, стол, шпиндель), найдено: {exports.Count}. "
                    + (OcctCadModelConverter.LastError ?? string.Empty));
            }

            definition.ExtraNodes.Clear();

            var components = new List<MeshComponent>(3);
            var pending = new List<(MachineNodeKind Kind, string NodeId, Point3D Centroid)>(3);

            try
            {
                for (int i = 0; i < 3; i++)
                {
                    CadComponentStlExport export = exports[i];
                    (_, MachineNodeKind kind, string nodeId) = DefaultOrder[i];

                    string stlFileName = store.CopyStlIntoProfile(profileId, export.TempStlPath, kind);
                    MachineNodeDefinition node = definition.GetNode(kind);
                    node.StlFileName = stlFileName;

                    string? fullPath = store.ResolveStlFullPath(profileId, stlFileName)
                        ?? throw new InvalidOperationException($"Не удалось сохранить STL: {stlFileName}");

                    Model3D? model = stlLoader.LoadAsync(fullPath).GetAwaiter().GetResult()
                        ?? throw new InvalidOperationException($"Не удалось прочитать STL: {fullPath}");

                    components.Add(new MeshComponent
                    {
                        Index = i,
                        DisplayName = export.DisplayName,
                        Model = model,
                        TriangleCount = 0
                    });

                    pending.Add((kind, nodeId, StlModelMetrics.GetCenter(model)));
                }
            }
            finally
            {
                foreach (CadComponentStlExport export in exports)
                {
                    MeshComponentFileExporter.TryDelete(export.TempStlPath);
                }
            }

            StlAnalysisResult sharedScale = MeshAssemblyPlacement.ComputeSharedScale(components);
            var physicalHome = definition.GetPhysicalHomePosition();
            IReadOnlyDictionary<string, Transform3D> transforms = KinematicChainSolver.SolveTransforms(
                definition,
                physicalHome.X,
                physicalHome.Y,
                physicalHome.Z);

            foreach ((MachineNodeKind kind, string nodeId, Point3D centroid) in pending)
            {
                MachineNodeDefinition node = definition.GetNode(kind);
                MachineGeometryPoint offset = MeshAssemblyPlacement.ComputeMeshOffset(
                    transforms,
                    nodeId,
                    centroid,
                    sharedScale.SuggestedMeshScale);

                node.StlSourceMaxExtent = sharedScale.SourceMaxExtent;
                node.TargetMaxExtentMm = sharedScale.SuggestedTargetMaxExtentMm;
                node.MeshScale = sharedScale.SuggestedMeshScale;
                node.MeshRotationDegrees = MachineGeometryPoint.Zero;
                node.MeshOffset = offset;
                node.SyncMeshScaleFromTarget();
            }

            definition.SyncHomePositionFromAxes();
            MachineNodeColors.ApplyDefaultBuiltInMeshColors(definition);

            var meshMap = new Dictionary<string, Model3D>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 3; i++)
            {
                (_, _, string nodeId) = DefaultOrder[i];
                meshMap[nodeId] = components[i].Model;
            }

            definition.SetMcsOriginScene(
                MachineAssemblyMetrics.ComputeSceneBoundsCenter(
                    definition,
                    meshMap,
                    physicalHome.X,
                    physicalHome.Y,
                    physicalHome.Z));

            definition.NormalizeAfterLoad();
            store.Save(definition);
        }
    }
}
