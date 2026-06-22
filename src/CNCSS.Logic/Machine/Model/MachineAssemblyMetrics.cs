using System.Windows.Media.Media3D;
using CNCSS.Vis;

namespace CNCSS.Machine.Model
{
    /// <summary>Метрики сборки станка в координатах сцены (мм).</summary>
    public static class MachineAssemblyMetrics
    {
        /// <summary>
        /// Центр общего bounding box всех узлов с мешами в абсолютных координатах сцены
        /// при заданной физической позе осей.
        /// </summary>
        public static MachineGeometryPoint ComputeSceneBoundsCenter(
            MachineDefinition definition,
            IReadOnlyDictionary<string, Model3D> meshesByNodeId,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentNullException.ThrowIfNull(meshesByNodeId);
            if (meshesByNodeId.Count == 0)
            {
                return MachineGeometryPoint.Zero;
            }

            IReadOnlyDictionary<string, Transform3D> kinematic =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);

            Rect3D combined = Rect3D.Empty;
            bool hasAny = false;

            foreach (KeyValuePair<string, Model3D> pair in meshesByNodeId)
            {
                if (pair.Value == null || !kinematic.TryGetValue(pair.Key, out Transform3D? nodeTransform))
                {
                    continue;
                }

                Transform3D meshLocal = ResolveMeshLocalTransform(definition, pair.Key);
                Rect3D localBounds = StlModelMetrics.ComputeBoundsRecursive(pair.Value);
                if (localBounds.IsEmpty)
                {
                    continue;
                }

                Rect3D worldBounds = TransformBounds(localBounds, meshLocal, nodeTransform);
                combined = hasAny ? Rect3D.Union(combined, worldBounds) : worldBounds;
                hasAny = true;
            }

            if (!hasAny)
            {
                return MachineGeometryPoint.Zero;
            }

            return new MachineGeometryPoint
            {
                X = combined.X + combined.SizeX * 0.5,
                Y = combined.Y + combined.SizeY * 0.5,
                Z = combined.Z + combined.SizeZ * 0.5
            };
        }

        private static Transform3D ResolveMeshLocalTransform(MachineDefinition definition, string nodeId)
        {
            if (definition.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                return MachineNodeMeshTransforms.BuildMeshLocalTransform(builtIn);
            }

            if (definition.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                return MachineNodeMeshTransforms.BuildMeshLocalTransform(extra);
            }

            return Transform3D.Identity;
        }

        private static Rect3D TransformBounds(Rect3D local, Transform3D meshLocal, Transform3D nodeToWorld)
        {
            Point3D[] corners =
            [
                new(local.X, local.Y, local.Z),
                new(local.X + local.SizeX, local.Y, local.Z),
                new(local.X, local.Y + local.SizeY, local.Z),
                new(local.X + local.SizeX, local.Y + local.SizeY, local.Z),
                new(local.X, local.Y, local.Z + local.SizeZ),
                new(local.X + local.SizeX, local.Y, local.Z + local.SizeZ),
                new(local.X, local.Y + local.SizeY, local.Z + local.SizeZ),
                new(local.X + local.SizeX, local.Y + local.SizeY, local.Z + local.SizeZ)
            ];

            bool hasAny = false;
            Rect3D combined = Rect3D.Empty;
            foreach (Point3D corner in corners)
            {
                Point3D inNode = MachineNodeMeshTransforms.TransformPoint(meshLocal, corner);
                Point3D world = MachineNodeMeshTransforms.TransformPoint(nodeToWorld, inNode);
                var r = new Rect3D(world, new Size3D(0, 0, 0));
                combined = hasAny ? Rect3D.Union(combined, r) : r;
                hasAny = true;
            }

            return combined;
        }
    }
}
