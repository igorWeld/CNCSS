namespace CNCSS.GpuVerification;

/// <summary>Статический фасад: проба GPU и офлайновое/пакетное вычисление маски занятости.</summary>
public static class GpuVerificationEngine
{
    private static GpuVerificationProbeResult _lastProbe;
    private static readonly object ProbeLock = new();
    private static double _lastEstimatedGpuLoadPercent;
    private const double TotalGpuMemoryBudgetFraction = 0.90;
    private const int BytesPerVoxelOccupancyPipeline = sizeof(uint) * 2; // UAV + staging readback

    public static GpuVerificationProbeResult Probe()
    {
        lock (ProbeLock)
        {
            _lastProbe = D3d11OccupancyGpuContext.ProbeHardware();
            return _lastProbe;
        }
    }

    public static GpuVerificationProbeResult LastProbe => _lastProbe;
    public static double LastEstimatedGpuLoadPercent => _lastEstimatedGpuLoadPercent;

    public static int ComputeAxisCapByVram(int requestedAxisCap)
    {
        int clampedRequested = requestedAxisCap <= 0 ? int.MaxValue : Math.Max(1, requestedAxisCap);
        ulong dedicatedBytes = LastProbe.DedicatedVideoMemoryBytes;
        ulong sharedBytes = LastProbe.SharedSystemMemoryBytes;
        ulong totalGpuAddressableBytes = dedicatedBytes + sharedBytes;
        if (totalGpuAddressableBytes == 0)
        {
            return clampedRequested;
        }

        double budgetBytes = totalGpuAddressableBytes * TotalGpuMemoryBudgetFraction;
        double maxCells = budgetBytes / BytesPerVoxelOccupancyPipeline;
        if (maxCells <= 1)
        {
            return 1;
        }

        int axisByBudget = Math.Max(1, (int)Math.Floor(Math.Cbrt(maxCells)));
        return Math.Min(clampedRequested, axisByBudget);
    }

    public static ulong ComputeGpuMemoryBudgetBytes()
    {
        ulong dedicatedBytes = LastProbe.DedicatedVideoMemoryBytes;
        ulong sharedBytes = LastProbe.SharedSystemMemoryBytes;
        ulong totalGpuAddressableBytes = dedicatedBytes + sharedBytes;
        if (totalGpuAddressableBytes == 0)
        {
            return 0;
        }

        double budgetBytes = totalGpuAddressableBytes * TotalGpuMemoryBudgetFraction;
        return (ulong)Math.Max(0, Math.Floor(budgetBytes));
    }

    public static double EstimateOccupancyMemoryLoadPercent(int dimX, int dimY, int dimZ)
    {
        if (dimX <= 0 || dimY <= 0 || dimZ <= 0)
        {
            return 0;
        }

        ulong budgetBytes = ComputeGpuMemoryBudgetBytes();
        if (budgetBytes == 0)
        {
            return 0;
        }

        long totalCells = (long)dimX * dimY * dimZ;
        double requiredBytes = totalCells * BytesPerVoxelOccupancyPipeline;
        return Math.Clamp(requiredBytes * 100.0 / budgetBytes, 0.0, 100.0);
    }

    public static void SetEstimatedGpuLoadPercent(int dimX, int dimY, int dimZ)
    {
        _lastEstimatedGpuLoadPercent = EstimateOccupancyMemoryLoadPercent(dimX, dimY, dimZ);
    }

    /// <summary>
    /// Симуляция занятости: ненулевое значение = материал. Индексация ix + dimX * (iy + dimY * iz).
    /// </summary>
    public static bool TryComputeOccupancy(
        GpuStockBounds bounds,
        double resolutionMm,
        int maxVoxelsPerAxis,
        IReadOnlyList<GpuCylinderCut> cuts,
        IProgress<(int Done, int Total)>? progress,
        out uint[]? occupancy,
        out int dimX,
        out int dimY,
        out int dimZ,
        out string? errorMessage)
    {
        occupancy = null;
        dimX = dimY = dimZ = 0;

        double width = bounds.MaxX - bounds.MinX;
        double depth = bounds.MaxY - bounds.MinY;
        double height = bounds.MaxZ - bounds.MinZ;
        dimX = Math.Max(1, (int)(width / resolutionMm));
        dimY = Math.Max(1, (int)(depth / resolutionMm));
        dimZ = Math.Max(1, (int)(height / resolutionMm));

        int effectiveAxisCap = ComputeAxisCapByVram(maxVoxelsPerAxis);
        if (dimX > effectiveAxisCap || dimY > effectiveAxisCap || dimZ > effectiveAxisCap)
        {
            errorMessage =
                $"Сетка {dimX}×{dimY}×{dimZ} превышает лимит по оси ({effectiveAxisCap} вокселей, 90% VRAM budget) — используется CPU.";
            return false;
        }

        long totalCells = (long)dimX * dimY * dimZ;
        if (totalCells > int.MaxValue / 4)
        {
            errorMessage = "Слишком большой объём для GPU-буфера.";
            return false;
        }

        try
        {
            SetEstimatedGpuLoadPercent(dimX, dimY, dimZ);
            using var ctx = new D3d11OccupancyGpuContext(dimX, dimY, dimZ);
            ctx.InitializeOccupancy();
            int reportEvery = Math.Max(1, cuts.Count / 50);
            for (int i = 0; i < cuts.Count; i++)
            {
                ctx.ApplyCut(cuts[i], bounds, resolutionMm);
                if ((i + 1) % reportEvery == 0 || i + 1 == cuts.Count)
                {
                    progress?.Report((i + 1, cuts.Count));
                }
            }

            occupancy = ctx.ReadOccupancyToCpu();
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            occupancy = null;
            return false;
        }
    }
}
