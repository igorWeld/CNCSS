namespace CNCSS.GpuVerification;

/// <summary>Параллелепипед заготовки в мировых координатах (мм), как в финальном CPU-расчёте.</summary>
public readonly record struct GpuStockBounds(
    double MinX,
    double MaxX,
    double MinY,
    double MaxY,
    double MinZ,
    double MaxZ);
