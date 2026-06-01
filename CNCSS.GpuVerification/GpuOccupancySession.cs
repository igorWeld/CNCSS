namespace CNCSS.GpuVerification;

/// <summary>
/// Сессия GPU-верификации на сетке текущего разрешения симуляции: буфер держится в VRAM между кадрами,
/// каждый отрезок резания добавляется через compute без полного офлайнового повтора УП.
/// </summary>
public sealed class GpuOccupancySession : IDisposable
{
    private readonly D3d11OccupancyGpuContext _gpu;
    private readonly GpuStockBounds _bounds;
    private readonly double _resolutionMm;

    internal GpuOccupancySession(D3d11OccupancyGpuContext gpu, GpuStockBounds bounds, double resolutionMm)
    {
        _gpu = gpu;
        _bounds = bounds;
        _resolutionMm = resolutionMm;
    }

    /// <summary>Пробует создать сессию для заданной заготовки и шага сетки. При ошибке или лимите — null.</summary>
    public static bool TryBegin(
        GpuStockBounds bounds,
        double resolutionMm,
        int maxVoxelsPerAxis,
        out GpuOccupancySession? session,
        out string? errorMessage)
    {
        session = null;
        GpuVerificationEngine.Probe();

        double width = bounds.MaxX - bounds.MinX;
        double depth = bounds.MaxY - bounds.MinY;
        double height = bounds.MaxZ - bounds.MinZ;
        int dimX = Math.Max(1, (int)(width / resolutionMm));
        int dimY = Math.Max(1, (int)(depth / resolutionMm));
        int dimZ = Math.Max(1, (int)(height / resolutionMm));

        int effectiveAxisCap = GpuVerificationEngine.ComputeAxisCapByVram(maxVoxelsPerAxis);
        if (dimX > effectiveAxisCap || dimY > effectiveAxisCap || dimZ > effectiveAxisCap)
        {
            errorMessage =
                $"Сетка симуляции {dimX}×{dimY}×{dimZ} превышает лимит по оси ({effectiveAxisCap}, 90% VRAM budget); верификация на GPU недоступна.";
            return false;
        }

        long totalCells = (long)dimX * dimY * dimZ;
        if (totalCells > int.MaxValue / 4)
        {
            errorMessage = "Слишком большой объём для GPU-буфера.";
            return false;
        }

        if (!GpuVerificationEngine.LastProbe.IsAvailable)
        {
            errorMessage = GpuVerificationEngine.LastProbe.Message ?? "GPU compute недоступен.";
            return false;
        }

        D3d11OccupancyGpuContext? gpu = null;
        try
        {
            GpuVerificationEngine.SetEstimatedGpuLoadPercent(dimX, dimY, dimZ);
            gpu = new D3d11OccupancyGpuContext(dimX, dimY, dimZ);
            gpu.InitializeOccupancy();
            session = new GpuOccupancySession(gpu, bounds, resolutionMm);
            gpu = null;
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            session = null;
            gpu?.Dispose();
            return false;
        }
    }

    /// <summary>Размер сетки (для отладки и согласования с воксельной заготовкой на CPU).</summary>
    public (int Dx, int Dy, int Dz) GridSize => (_gpu.DimX, _gpu.DimY, _gpu.DimZ);

    /// <inheritdoc cref="D3d11OccupancyGpuContext.ApplyCut"/>
    public void ApplyProgramCut(GpuCylinderCut cut) => _gpu.ApplyCut(cut, _bounds, _resolutionMm);

    public void Dispose() => _gpu.Dispose();
}
