using System.Windows.Media.Media3D;

namespace CNCSS.Machine.Model
{
    /// <summary>Spindle holder point vs tool tip (TCP) along machine Z.</summary>
    public static class ToolHolderKinematics
    {
        /// <summary>Collet / holder point: TCP in MCS + optional stick-out along +Z.</summary>
        public static Point3D ComputeToolHolderPoint(
            MachineDefinition definition,
            double physicalX,
            double physicalY,
            double physicalZ,
            double stickOutMm = 0)
        {
            Point3D tcp = ToolMountMcsHelper.ComputeTcpPhysical(definition, physicalX, physicalY, physicalZ);
            return stickOutMm > 1e-9
                ? new Point3D(tcp.X, tcp.Y, tcp.Z + stickOutMm)
                : tcp;
        }

        /// <summary>TCP below holder along -Z (vertical spindle).</summary>
        public static Point3D ComputeToolTipFromHolder(Point3D holderWorld, double stickOutMm) =>
            new(holderWorld.X, holderWorld.Y, holderWorld.Z - Math.Max(0, stickOutMm));

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
