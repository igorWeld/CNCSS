using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CNCSS.Logic.Voxel.Adaptive;

/// <summary>Параметры инструмента для симуляции съёма материала.</summary>
public sealed class VoxelTool
{
    public double CutDiameter { get; init; } = 10.0;

    public double CutLength { get; init; } = 20.0;

    public double ShankDiameter { get; init; } = 10.0;

    public double ShankLength { get; init; } = 40.0;

    public Color CuttingColor { get; init; } = Colors.Gold;

    public Color ShankColor { get; init; } = Colors.SlateGray;

    public double CutRadius => Math.Max(0.0, CutDiameter * 0.5);

    /// <summary>
    /// Вертикальный инструмент вдоль -Z: торец в tipPos, режущая часть идет вверх (+Z) на CutLength.
    /// </summary>
    public bool IsInsideCuttingCylinder(Point3D point, Point3D tipPos)
    {
        if (point.Z < tipPos.Z || point.Z > tipPos.Z + CutLength)
        {
            return false;
        }

        double dx = point.X - tipPos.X;
        double dy = point.Y - tipPos.Y;
        return dx * dx + dy * dy <= CutRadius * CutRadius;
    }
}
