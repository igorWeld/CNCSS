using System.Windows.Media.Media3D;

namespace CNCSS.Vis
{
    /// <summary>One solid/mesh body extracted from an import file.</summary>
    public sealed class MeshComponent
    {
        public required int Index { get; init; }

        public required string DisplayName { get; init; }

        public required Model3D Model { get; init; }

        public string? SourceHint { get; init; }

        public int TriangleCount { get; init; }

        public Rect3D Bounds => StlModelMetrics.ComputeBoundsRecursive(Model);
    }
}
