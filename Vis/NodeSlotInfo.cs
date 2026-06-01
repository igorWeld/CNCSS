using System.Windows.Media.Media3D;

namespace CNCSS.Vis
{
    public sealed class NodeSlotInfo
    {
        public required string NodeId { get; init; }
        public required Transform3D MeshToWorld { get; init; }
        public required Transform3D KinematicTransform { get; init; }
        public required Transform3D MeshTransform { get; init; }
    }
}
