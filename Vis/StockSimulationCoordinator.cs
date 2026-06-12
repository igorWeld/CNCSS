using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.GpuVerification;

namespace CNCSS.Vis
{
    /// <summary>
    /// Создаёт воксельную заготовку и связанную GPU-сессию без привязки к WPF.
    /// При наличии <see cref="StockConstructorConfig"/> маска занятости повторяет форму (прямоугольник, шестигранник, круг, труба).
    /// </summary>
    public sealed class StockSimulationCoordinator
    {
        public StockRuntime CreateRuntime(
            StockVolumeConfig cfg,
            double resolutionMm,
            bool enableGpuVerification,
            System.Windows.Media.Color? defaultStockColor = null,
            StockConstructorConfig? constructorConfig = null)
        {
            var profile = VoxelSimulationProfile.ForResolution(resolutionMm);
            var center = new Point3D((cfg.MinX + cfg.MaxX) / 2, (cfg.MinY + cfg.MaxY) / 2, 0);
            double width = cfg.MaxX - cfg.MinX;
            double depth = cfg.MaxY - cfg.MinY;
            double height = cfg.MaxZ - cfg.MinZ;

            VoxelStock stock;
            if (constructorConfig != null)
            {
                uint[] occ = BuildOccupancyMask(constructorConfig, cfg, profile.ResolutionMm);
                stock = VoxelStock.FromGpuOccupancyMask(width, depth, height, profile.ResolutionMm, center, cfg.MaxZ, occ, defaultStockColor);
            }
            else
            {
                stock = new VoxelStock(width, depth, height, profile.ResolutionMm, center, cfg.MaxZ, defaultStockColor);
            }

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

        /// <summary>Плотная маска 1/0 по ячейкам сетки в габаритах bounds (индексация как в GpuVerification).</summary>
        private static uint[] BuildOccupancyMask(StockConstructorConfig sc, StockVolumeConfig bounds, double res)
        {
            double width = bounds.MaxX - bounds.MinX;
            double depth = bounds.MaxY - bounds.MinY;
            double height = bounds.MaxZ - bounds.MinZ;

            int sx = Math.Max(1, (int)(width / res));
            int sy = Math.Max(1, (int)(depth / res));
            int sz = Math.Max(1, (int)(height / res));

            double cx = (bounds.MinX + bounds.MaxX) * 0.5;
            double cy = (bounds.MinY + bounds.MaxY) * 0.5;
            double minX = cx - width * 0.5;
            double minY = cy - depth * 0.5;

            var occ = new uint[checked(sx * sy * sz)];

            double rOuter = Math.Max(1e-9, sc.Param1Mm * 0.5);
            double rInner = Math.Max(0.0, sc.Param2Mm * 0.5);
            double rOuter2 = rOuter * rOuter;
            double rInner2 = rInner * rInner;

            // Шестигранник: полуплоскости |dot(p, n)| ≤ apothem для n под углами 0°, 60°, 120°.
            double apothem = Math.Max(1e-9, sc.Param1Mm * 0.5);
            const double half = 0.5;
            double s3_2 = Math.Sqrt(3.0) * 0.5;

            int idx = 0;
            for (int iz = 0; iz < sz; iz++)
            {
                for (int iy = 0; iy < sy; iy++)
                {
                    double y = minY + (iy + 0.5) * res;
                    double dy = y - cy;
                    for (int ix = 0; ix < sx; ix++, idx++)
                    {
                        double x = minX + (ix + 0.5) * res;
                        double dx = x - cx;

                        bool inside = sc.ShapeType switch
                        {
                            StockShapeType.Rectangular => true,
                            StockShapeType.Round => (dx * dx + dy * dy) <= rOuter2,
                            StockShapeType.Tube =>
                                (dx * dx + dy * dy) <= rOuter2 && (dx * dx + dy * dy) >= rInner2,
                            StockShapeType.Hexagonal =>
                                Math.Abs(dx) <= apothem &&
                                Math.Abs(dx * half + dy * s3_2) <= apothem &&
                                Math.Abs(-dx * half + dy * s3_2) <= apothem,
                            _ => true
                        };

                        occ[idx] = inside ? 1u : 0u;
                    }
                }
            }

            return occ;
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
