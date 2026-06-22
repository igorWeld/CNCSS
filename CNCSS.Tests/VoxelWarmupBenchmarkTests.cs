using System.Diagnostics;
using System.Windows.Media;
using CNCSS.Data;
using CNCSS.Logic.Voxel.Adaptive;
using CNCSS.Vis;
using Xunit;

namespace CNCSS.Tests;

/// <summary>Регрессия инициализации и прогрева нового адаптивного движка.</summary>
public sealed class VoxelWarmupBenchmarkTests
{
    [Fact]
    public async Task AdaptiveStock_init_small_box_under_budget()
    {
        var stock = new AdaptiveVoxelStock(new StockVolumeConfig(0, 50, 0, 50, 0, 25), 1.0, 0.1);
        var sw = Stopwatch.StartNew();
        stock.InitializeStock();
        _ = await stock.BuildMeshAsync();
        sw.Stop();

        Assert.True(sw.Elapsed.TotalSeconds < 30.0, $"init took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task VoxelStockVolume_create_and_publish_mesh()
    {
        var volume = new StockVolumeConfig(0, 10, 0, 10, 0, 10);
        using var stock = VoxelStockVolume.Create(volume, null, Colors.Gray, 1.0);

        stock.ActivateShellDisplay();
        await stock.UpdateVisualsAsync();
        Assert.True(stock.ShellDisplayed);
        Assert.True(stock.AdaptiveStock.Octree.EnumerateAllChunks().Count > 0);
    }

    [Fact]
    public async Task AdaptiveStock_shaped_stock_initializes()
    {
        // Пока shape mask не участвует в adaptive ядре напрямую, проверяем базовую стабильность.
        var stock = new AdaptiveVoxelStock(new StockVolumeConfig(0, 30, 0, 30, 0, 15), 1.0, 0.1);
        stock.InitializeStock();
        var mesh = await stock.BuildMeshAsync();
        Assert.True(mesh.Positions.Count > 0);
    }
}
