using System.Windows.Media.Media3D;
using CNCSS.Vis;

namespace CNCSS.Machine.Model
{
    public enum MeshAttachmentPreset
    {
        Bottom,
        Center,
        Top
    }

    /// <summary>Преобразования точек привязки и STL-пресетов в координаты сцены.</summary>
    public static class MachineAttachmentSceneMath
    {
        public static MachineGeometryPoint PresetToLocalPoint(MeshAttachmentPreset preset, Rect3D meshBounds) =>
            preset switch
            {
                MeshAttachmentPreset.Bottom => StlMeshBoundsHelper.BottomCenter(meshBounds),
                MeshAttachmentPreset.Center => StlMeshBoundsHelper.Center(meshBounds),
                MeshAttachmentPreset.Top => StlMeshBoundsHelper.TopCenter(meshBounds),
                _ => StlMeshBoundsHelper.Center(meshBounds)
            };

        public static bool TryGetMeshPresetScenePoint(
            MachineDefinition definition,
            string nodeId,
            MeshAttachmentPreset preset,
            Model3D mesh,
            double physicalX,
            double physicalY,
            double physicalZ,
            out Point3D scene)
        {
            scene = default;
            if (mesh == null)
            {
                return false;
            }

            Rect3D bounds = StlModelMetrics.ComputeBoundsRecursive(mesh);
            if (bounds.IsEmpty)
            {
                return false;
            }

            MachineGeometryPoint local = PresetToLocalPoint(preset, bounds);
            scene = MeshLocalToScene(definition, nodeId, local, physicalX, physicalY, physicalZ);
            return true;
        }

        public static Point3D MeshLocalToScene(
            MachineDefinition definition,
            string nodeId,
            MachineGeometryPoint meshLocal,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(definition, physicalX, physicalY, physicalZ);

            if (!transforms.TryGetValue(nodeId, out Transform3D? kinematic))
            {
                kinematic = Transform3D.Identity;
            }

            Transform3D meshLocalTransform = ResolveMeshLocalTransform(definition, nodeId);
            Point3D localPoint = new(meshLocal.X, meshLocal.Y, meshLocal.Z);
            Point3D inNode = MachineNodeMeshTransforms.TransformPoint(meshLocalTransform, localPoint);
            Point3D assembly = MachineNodeMeshTransforms.TransformPoint(kinematic, inNode);
            var mcs = MachineMcsCoordinates.GetMcsZeroOffset(definition);
            return new Point3D(
                assembly.X + mcs.X,
                assembly.Y + mcs.Y,
                assembly.Z + mcs.Z);
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
    }

}
