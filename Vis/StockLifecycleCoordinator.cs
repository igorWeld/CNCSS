using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.GpuVerification;
using CNCSS.Machine.Model;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Жизненный цикл заготовки: габариты, параметрический меш, воксельный runtime.</summary>
    public sealed class StockLifecycleCoordinator
    {
        private readonly StockSimulationCoordinator _simulationCoordinator;

        public StockLifecycleCoordinator(StockSimulationCoordinator simulationCoordinator)
        {
            _simulationCoordinator = simulationCoordinator ?? throw new ArgumentNullException(nameof(simulationCoordinator));
        }

        public IStockVolume? Stock { get; set; }
        public StockCutWorker? CutWorker { get; set; }
        public GpuOccupancySession? GpuSession { get; set; }
        public VoxelSimulationProfile? ActiveProfile { get; private set; }

        public StockConstructorConfig? ConstructorConfig { get; set; }
        public WorkpiecePlacement.StockBounds? Bounds { get; set; }
        public bool DeferVoxelUntilRun { get; set; }
        public bool AnchoredToTable { get; set; }
        public bool BoundsAreTableLocal { get; set; }
        public bool AutoAlignToMount { get; set; } = true;

        /// <summary>Шаг воксельной сетки (мм) для runtime-симуляции и Cycle Start.</summary>
        public double VoxelResolutionMm { get; set; } = ProjectConstants.RES_MEDIUM;

        public void ReleaseRuntime()
        {
            CutWorker?.Dispose();
            CutWorker = null;
            GpuSession?.Dispose();
            GpuSession = null;
            Stock = null;
            ActiveProfile = null;
        }

        public static StockVolumeConfig ToVolumeConfig(WorkpiecePlacement.StockBounds bounds) =>
            new(bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY, bounds.MinZ, bounds.MaxZ);

        public MeshGeometry3D BuildParametricMesh(WorkpiecePlacement.StockBounds bounds)
        {
            if (ConstructorConfig is StockConstructorConfig sc)
            {
                MeshGeometry3D mesh = StockConstructorPreviewBuilder.BuildStockMeshFromBounds(sc, bounds);
                mesh.Freeze();
                return mesh;
            }

            return BuildBoxMesh(bounds);
        }

        public Color ResolveStockColor(Color? constructorColor, Color fallback = default)
        {
            if (constructorColor is Color c)
            {
                return c;
            }

            return fallback == default ? Colors.LightGray : fallback;
        }

        public bool TryEnsureVoxelRuntime(
            bool reuseRunningSimulation,
            bool enableGpuVerification,
            Color surfaceColor,
            out string? gpuError)
        {
            gpuError = null;
            if (reuseRunningSimulation &&
                Stock is VoxelStock existing &&
                !existing.IsCutsFrozen &&
                Math.Abs(existing.Resolution - VoxelResolutionMm) <= 1e-9)
            {
                return true;
            }

            ReleaseRuntime();

            if (Bounds is not WorkpiecePlacement.StockBounds bounds)
            {
                return false;
            }

            StockVolumeConfig volumeCfg = ToVolumeConfig(bounds);
            var runtime = _simulationCoordinator.CreateRuntime(
                volumeCfg,
                VoxelResolutionMm,
                enableGpuVerification,
                surfaceColor,
                ConstructorConfig);

            Stock = runtime.Stock;
            CutWorker = runtime.Worker;
            GpuSession = runtime.GpuSession;
            ActiveProfile = runtime.Profile;
            gpuError = runtime.GpuError;
            DeferVoxelUntilRun = false;
            return true;
        }

        private static MeshGeometry3D BuildBoxMesh(WorkpiecePlacement.StockBounds bounds)
        {
            double sx = Math.Max(0.001, bounds.MaxX - bounds.MinX);
            double sy = Math.Max(0.001, bounds.MaxY - bounds.MinY);
            double sz = Math.Max(0.001, bounds.MaxZ - bounds.MinZ);
            var center = new Point3D(
                (bounds.MinX + bounds.MaxX) * 0.5,
                (bounds.MinY + bounds.MaxY) * 0.5,
                (bounds.MinZ + bounds.MaxZ) * 0.5);
            var builder = new MeshBuilder(false, false);
            builder.AddBox(center, sx, sy, sz);
            var mesh = builder.ToMesh();
            mesh.Freeze();
            return mesh;
        }
    }
}
