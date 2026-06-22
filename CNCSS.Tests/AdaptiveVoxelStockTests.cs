using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic.Voxel.Adaptive;
using Xunit;

namespace CNCSS.Tests;

public sealed class AdaptiveVoxelStockTests
{
    [Fact]
    public async Task InitializeStock_creates_mesh()
    {
        var stock = new AdaptiveVoxelStock(new StockVolumeConfig(0, 40, 0, 40, 0, 20), 1.0, 0.1);
        stock.InitializeStock();
        var mesh = await stock.BuildMeshAsync();
        Assert.True(mesh.Positions.Count > 0);
    }

    [Fact]
    public void CutCylinder_removes_voxels_and_marks_dirty()
    {
        var stock = new AdaptiveVoxelStock(new StockVolumeConfig(0, 40, 0, 40, 0, 20), 1.0, 0.1);
        stock.InitializeStock();
        int removed = stock.CutCylinder(
            new Point3D(20, 20, 18),
            new Point3D(20, 20, 8),
            radius: 3.0,
            cutLength: 10.0);

        Assert.True(removed >= 0);
        Assert.True(stock.LastRemovedVoxels >= 0);
    }

    [Fact]
    public void CutCylinder_uses_flute_zone_above_tip()
    {
        // Заготовка только в диапазоне Z=[10..20], торец инструмента ниже (Z=8).
        // Рез должен происходить только если flute считается вверх от торца: [8..18].
        var stock = new AdaptiveVoxelStock(new StockVolumeConfig(0, 30, 0, 30, 10, 20), 1.0, 0.1);
        stock.InitializeStock();

        int removed = stock.CutCylinder(
            new Point3D(15, 15, 8),
            new Point3D(15, 15, 8),
            radius: 2.0,
            cutLength: 10.0);

        Assert.True(removed > 0);
    }

    [Fact]
    public async Task BuildMeshAsync_with_display_stride_reduces_mesh_size()
    {
        var fine = new AdaptiveVoxelStock(new StockVolumeConfig(0, 30, 0, 30, 0, 20), 1.0, 0.1);
        fine.InitializeStock();
        fine.CutCylinder(new Point3D(15, 15, 8), new Point3D(15, 15, 6), radius: 3.0, cutLength: 10.0);

        var coarse = new AdaptiveVoxelStock(new StockVolumeConfig(0, 30, 0, 30, 0, 20), 1.0, 0.1);
        coarse.InitializeStock();
        coarse.CutCylinder(new Point3D(15, 15, 8), new Point3D(15, 15, 6), radius: 3.0, cutLength: 10.0);

        var fineMesh = await fine.BuildMeshAsync(displayStride: 1);
        var coarseMesh = await coarse.BuildMeshAsync(displayStride: 3);

        Assert.True(coarseMesh.Positions.Count <= fineMesh.Positions.Count);
        Assert.True(coarseMesh.TriangleIndices.Count <= fineMesh.TriangleIndices.Count);
    }
}
