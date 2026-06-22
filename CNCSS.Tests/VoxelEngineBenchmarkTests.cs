using System.Diagnostics;
using System.Windows.Media;
using CNCSS.Data;
using CNCSS.Logic.Voxel;
using CNCSS.Logic.Voxel.Adaptive;
using CNCSS.Vis;
using Xunit;

namespace CNCSS.Tests;

/// <summary>Бенчмарки нового адаптивного воксельного движка.</summary>
public sealed class VoxelEngineBenchmarkTests
{
    [Fact]
    public void AdaptiveStock_cut_on_small_box_completes_within_budget()
    {
        var stock = new AdaptiveVoxelStock(new StockVolumeConfig(0, 40, 0, 40, 0, 20), 1.0, 0.1);
        stock.InitializeStock();

        var sw = Stopwatch.StartNew();
        int removed = stock.CutCylinder(
            new System.Windows.Media.Media3D.Point3D(20, 20, 20),
            new System.Windows.Media.Media3D.Point3D(21, 20, 20),
            radius: 5.0,
            cutLength: 20.0);
        sw.Stop();

        Assert.True(removed >= 0);
        Assert.True(sw.Elapsed.TotalMilliseconds < 400.0);
    }

    [Fact]
    public async Task AdaptiveStock_rectangular_init_has_mesh()
    {
        var stock = new AdaptiveVoxelStock(new StockVolumeConfig(0, 10, 0, 10, 0, 10), 1.0, 0.1);
        stock.InitializeStock();

        var mesh = await stock.BuildMeshAsync();
        Assert.True(mesh.Positions.Count > 0);
    }

    [Fact]
    public async Task VoxelStockVolume_apply_cut_uses_adaptive_engine()
    {
        var volume = new StockVolumeConfig(0, 20, 0, 20, 0, 10);
        using var stock = VoxelStockVolume.Create(volume, null, Colors.Gray, 1.0);

        stock.ApplyCutMotion(
            CutMotionDescriptor.Linear(
                new System.Windows.Media.Media3D.Point3D(10, 10, 10),
                new System.Windows.Media.Media3D.Point3D(10, 10, 9)),
            new FluteCutProfile { Radius = 3.0, FluteLength = 10.0 },
            Colors.Red);
        stock.ActivateShellDisplay();
        await stock.UpdateVisualsAsync();

        Assert.True(stock.AdaptiveStock.Octree.EnumerateAllChunks().Count > 0);
    }
}
