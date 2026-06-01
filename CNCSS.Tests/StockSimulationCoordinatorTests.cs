using CNCSS.Vis;

namespace CNCSS.Tests;

public sealed class StockSimulationCoordinatorTests
{
    [Fact]
    public void CreateRuntime_ReturnsStockWorkerAndProfile()
    {
        var coordinator = new StockSimulationCoordinator();

        using var runtime = DisposableStockRuntime.Wrap(coordinator.CreateRuntime(
            new StockVolumeConfig(-1, 1, -1, 1, -1, 1),
            enableGpuVerification: false));

        Assert.NotNull(runtime.Value.Stock);
        Assert.NotNull(runtime.Value.Worker);
        Assert.Null(runtime.Value.GpuSession);
        Assert.True(runtime.Value.Profile.ResolutionMm > 0);
    }

    private sealed class DisposableStockRuntime : IDisposable
    {
        private DisposableStockRuntime(StockRuntime value) => Value = value;

        public StockRuntime Value { get; }

        public static DisposableStockRuntime Wrap(StockRuntime value) => new(value);

        public void Dispose()
        {
            Value.Worker.Dispose();
            Value.GpuSession?.Dispose();
        }
    }
}
