using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Logic.Voxel
{
    public enum CutMotionKind
    {
        Linear = 0,
        Arc = 1
    }

    public enum FluteProfileKind
    {
        EndMill = 0,
        Drill = 1
    }

    /// <summary>Описание движения кончика инструмента для apply_cut.</summary>
    public sealed class CutMotionDescriptor
    {
        public Point3D Start { get; init; }
        public Point3D End { get; init; }
        public CutMotionKind Kind { get; init; } = CutMotionKind.Linear;
        public ArcGeometry? Arc { get; init; }

        public static CutMotionDescriptor Linear(Point3D start, Point3D end) =>
            new() { Start = start, End = end, Kind = CutMotionKind.Linear };

        public static CutMotionDescriptor FromArc(Point3D start, Point3D end, ArcGeometry arc) =>
            new() { Start = start, End = end, Kind = CutMotionKind.Arc, Arc = arc };
    }

    /// <summary>Геометрия режущей части (без хвостовика).</summary>
    public sealed class FluteCutProfile
    {
        public FluteProfileKind Kind { get; init; } = FluteProfileKind.EndMill;
        public double Radius { get; init; }
        public double FluteLength { get; init; }
        public double PointAngleDegrees { get; init; } = 118.0;
    }

    internal static class VoxelColorPacking
    {
        public static uint PackRgba(Color color) =>
            ((uint)color.R << 24) |
            ((uint)color.G << 16) |
            ((uint)color.B << 8) |
            color.A;
    }
}
