using System.Windows.Media.Media3D;

namespace CNCSS.Logic.Voxel.Adaptive;

/// <summary>Сегмент движения инструмента для kernel (линейный после аппроксимации дуг).</summary>
public sealed record VoxelMotionSegment(
    Point3D Start,
    Point3D End,
    bool IsCuttingMove,
    int SourceLineNumber);
