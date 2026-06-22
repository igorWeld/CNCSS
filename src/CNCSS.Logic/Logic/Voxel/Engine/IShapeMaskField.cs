using System.Windows.Media.Media3D;

namespace CNCSS.Logic.Voxel.Engine;

/// <summary>Начальная занятость заготовки в локальных координатах объёма (мм, origin = 0).</summary>
public interface IShapeMaskField
{
    bool Contains(Point3D volumeLocalMm);
    bool IsFullBoundingBox { get; }

    /// <summary>Точка внутри формы и ближе marginMm к границе занятости.</summary>
    bool IsNearBoundary(Point3D volumeLocalMm, double marginMm);
}
