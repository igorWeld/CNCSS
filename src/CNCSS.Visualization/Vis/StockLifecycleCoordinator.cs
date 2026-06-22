using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Data.Config;
using CNCSS.Logic.Voxel;
using CNCSS.Machine.Model;
using CNCSS.UI;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Жизненный цикл заготовки: параметрический меш и воксельный runtime (C# — в разработке).</summary>
    public sealed class StockLifecycleCoordinator
    {
        private readonly StockSimulationCoordinator _simulationCoordinator;

        public StockLifecycleCoordinator(StockSimulationCoordinator simulationCoordinator)
        {
            _simulationCoordinator = simulationCoordinator ?? throw new ArgumentNullException(nameof(simulationCoordinator));
        }

        public IStockVolume? Stock { get; set; }

        public double VoxelResolutionMm { get; set; } = VoxelConstants.VoxelResolutionMm;

        public double AdaptiveDisplayGridMm { get; set; } = VoxelConstants.DisplayGridDefaultMm;

        public StockConstructorConfig? ConstructorConfig { get; set; }

        public WorkpiecePlacement.StockBounds? Bounds { get; set; }

        public bool DeferVoxelUntilRun { get; set; }

        /// <summary>Оболочка/materialize откладывается до подвода инструмента (&lt;10 мм).</summary>
        public bool DeferVoxelShellUntilApproach { get; set; }

        public bool AnchoredToTable { get; set; }

        public bool BoundsAreTableLocal { get; set; }

        public bool AutoAlignToMount { get; set; } = true;

        public void ReleaseRuntime()
        {
            if (Stock is IDisposable disposable)
            {
                disposable.Dispose();
            }

            Stock = null;
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

        public Color ResolveStockColor(StockColor? constructorColor, Color fallback = default)
        {
            if (constructorColor is StockColor sc)
            {
                return sc.ToWpfColor();
            }

            return fallback == default ? Colors.LightGray : fallback;
        }

        public VoxelStockVolume? TryCreateVoxelHost()
        {
            if (Bounds is not WorkpiecePlacement.StockBounds bounds)
            {
                return null;
            }

            return _simulationCoordinator.CreateHost(ToVolumeConfig(bounds), ConstructorConfig, AdaptiveDisplayGridMm);
        }

        public bool TryAttachVoxelHost(VoxelStockVolume host, Color surfaceColor)
        {
            ReleaseRuntime();
            StockVolumeConfig? volumeCfg = Bounds is WorkpiecePlacement.StockBounds bounds
                ? ToVolumeConfig(bounds)
                : null;
            Stock = _simulationCoordinator.WrapHost(host, surfaceColor, ConstructorConfig, volumeCfg);
            DeferVoxelUntilRun = true;
            DeferVoxelShellUntilApproach = true;
            return true;
        }

        public bool TryEnsureVoxelRuntime(bool reuseRunningSimulation, Color surfaceColor, Action? pumpUi = null)
        {
            if (reuseRunningSimulation && Stock is VoxelStockVolume { IsCutsFrozen: false })
            {
                return true;
            }

            ReleaseRuntime();

            if (Bounds is not WorkpiecePlacement.StockBounds bounds)
            {
                return false;
            }

            StockVolumeConfig volumeCfg = ToVolumeConfig(bounds);
            Task<StockRuntime> createTask = Task.Run(() =>
                _simulationCoordinator.CreateRuntime(
                    volumeCfg,
                    VoxelResolutionMm,
                    surfaceColor,
                    ConstructorConfig,
                    AdaptiveDisplayGridMm));

            while (!createTask.IsCompleted)
            {
                pumpUi?.Invoke();
                Thread.Sleep(1);
            }

            Stock = createTask.GetAwaiter().GetResult().Stock;
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
