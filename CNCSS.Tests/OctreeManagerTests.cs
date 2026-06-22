using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic.Voxel.Adaptive;
using Xunit;

namespace CNCSS.Tests;

public sealed class OctreeManagerTests
{
    [Fact]
    public void InitializeBoundaryChunks_creates_only_boundary_chunks_initially()
    {
        var manager = new OctreeManager(new StockVolumeConfig(0, 40, 0, 40, 0, 20), 1.0, 0.1);
        manager.InitializeBoundaryChunks();
        IReadOnlyList<VoxelChunk> chunks = manager.EnumerateAllChunks();

        Assert.NotEmpty(chunks);
        // Не должно быть пустой инициализации внутреннего объёма.
        Assert.True(chunks.Count < 3000);
    }

    [Fact]
    public void GetOrCreateChunk_materializes_interior_on_demand()
    {
        var manager = new OctreeManager(new StockVolumeConfig(0, 40, 0, 40, 0, 20), 1.0, 0.1);
        manager.InitializeBoundaryChunks();
        int before = manager.EnumerateAllChunks().Count;

        manager.GetOrCreateChunk(20, 20, 10, 0.1);
        int after = manager.EnumerateAllChunks().Count;

        Assert.True(after >= before);
    }

    [Fact]
    public void GetOrCreateChunksInAabb_returns_chunks_intersecting_box()
    {
        var manager = new OctreeManager(new StockVolumeConfig(0, 100, 0, 100, 0, 50), 1.0, 0.1);
        Rect3D aabb = new Rect3D(45, 45, 20, 10, 10, 10);
        IReadOnlyList<VoxelChunk> chunks = manager.GetOrCreateChunksInAabb(aabb, 0.1);

        Assert.NotEmpty(chunks);
    }

    [Fact]
    public void GetOrCreateChunk_large_stock_reaches_requested_fine_voxel()
    {
        var manager = new OctreeManager(new StockVolumeConfig(0, 100, 0, 100, 0, 50), 1.0, 0.1);
        VoxelChunk chunk = manager.GetOrCreateChunk(50, 50, 25, 0.1);

        Assert.True(chunk.VoxelSizeMm <= 0.11);
    }
}
