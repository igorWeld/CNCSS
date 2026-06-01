using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.GpuVerification;

namespace CNCSS.Vis
{
    /// <summary>Создаёт runtime-заготовку и связанную GPU-сессию без привязки к WPF controls.</summary>
    public sealed class StockSimulationCoordinator
    {
        public StockRuntime CreateRuntime(StockVolumeConfig cfg, bool enableGpuVerification)
        {
            var profile = VoxelSimulationProfile.ForResolution(ProjectConstants.STOCK_VOXEL_RESOLUTION_MM);
            var center = new Point3D((cfg.MinX + cfg.MaxX) / 2, (cfg.MinY + cfg.MaxY) / 2, 0);
            var stock = new VoxelStock(
                cfg.MaxX - cfg.MinX,
                cfg.MaxY - cfg.MinY,
                cfg.MaxZ - cfg.MinZ,
                profile.ResolutionMm,
                center,
                cfg.MaxZ);

            var worker = new StockCutWorker(stock, profile.CutStepMm);
            GpuOccupancySession? gpuSession = null;
            string? gpuError = null;

            if (enableGpuVerification)
            {
                var bounds = new GpuStockBounds(cfg.MinX, cfg.MaxX, cfg.MinY, cfg.MaxY, cfg.MinZ, cfg.MaxZ);
                if (!GpuOccupancySession.TryBegin(bounds, profile.ResolutionMm, 0, out gpuSession, out gpuError))
                {
                    gpuSession = null;
                }
            }

            return new StockRuntime(stock, worker, gpuSession, profile, gpuError);
        }
    }

    public readonly record struct StockVolumeConfig(double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ);

    public sealed record StockRuntime(
        IStockVolume Stock,
        StockCutWorker Worker,
        GpuOccupancySession? GpuSession,
        VoxelSimulationProfile Profile,
        string? GpuError);
}
