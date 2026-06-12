using System.Windows.Media.Media3D;

namespace CNCSS.Machine.Model
{
    /// <summary>Spindle holder point vs tool tip (TCP) along machine Z.</summary>
    public static class ToolHolderKinematics
    {
        /// <summary>Плоскость крепления (TCP / торец хвостовика) в MCS.</summary>
        public static Point3D ComputeToolHolderPoint(
            MachineDefinition definition,
            double physicalX,
            double physicalY,
            double physicalZ,
            double stickOutMm = 0)
        {
            _ = stickOutMm;
            return ToolMountMcsHelper.ComputeTcpPhysical(definition, physicalX, physicalY, physicalZ);
        }

        /// <summary>Режущий конец ниже плоскости крепления вдоль -Z (вертикальный шпиндель).</summary>
        public static Point3D ComputeToolTipFromHolder(Point3D holderPlaneMcs, double stickOutMm) =>
            new(holderPlaneMcs.X, holderPlaneMcs.Y, holderPlaneMcs.Z - Math.Max(0, stickOutMm));

        private static Transform3D BuildMeshTransformForNode(MachineDefinition definition, string nodeId)
        {
            if (definition.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                return MachineNodeMeshTransforms.BuildMeshLocalTransform(builtIn);
            }

            if (definition.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                return MachineNodeMeshTransforms.BuildMeshLocalTransform(extra);
            }

            return MachineNodeMeshTransforms.BuildMeshLocalTransform(definition.Spindle);
        }
    }
}
