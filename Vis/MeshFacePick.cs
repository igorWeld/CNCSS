using System.Windows.Media.Media3D;

namespace CNCSS.Vis
{
    public sealed class MeshFacePick
    {
        public required string NodeId { get; init; }
        public required Point3D PointWorld { get; init; }
        public required Vector3D NormalWorld { get; init; }
        public required Point3D PointMeshLocal { get; init; }
        public required Vector3D NormalMeshLocal { get; init; }

        /// <summary>Hit triangle in <see cref="SourceMesh"/>; used to highlight the full coplanar face.</summary>
        public int SeedTriangleIndex { get; init; } = -1;

        public MeshGeometry3D? SourceMesh { get; init; }
    }
}
