namespace CNCSS.Data;

/// <summary>Расстояние инструмента до заготовки (table-local AABB).</summary>
public static class StockApproachHelper
{
    /// <summary>Минимальное расстояние от точки до границ заготовки (0 внутри объёма).</summary>
    public static double DistancePointToBoundsMm(
        double xMm,
        double yMm,
        double zMm,
        StockVolumeConfig bounds)
    {
        double dx = AxisGap(xMm, bounds.MinX, bounds.MaxX);
        double dy = AxisGap(yMm, bounds.MinY, bounds.MaxY);
        double dz = AxisGap(zMm, bounds.MinZ, bounds.MaxZ);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static double AxisGap(double value, double min, double max)
    {
        if (value < min)
        {
            return min - value;
        }

        if (value > max)
        {
            return value - max;
        }

        return 0;
    }
}
