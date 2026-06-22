using CNCSS.Data;
using CNCSS.Logic.Voxel.Adaptive;
using Xunit;

namespace CNCSS.Tests;

public sealed class StockSnapshotStoreTests
{
    [Fact]
    public async Task SaveLoad_roundtrip_preserves_chunk_count()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"cncss-stock-{Guid.NewGuid():N}.json");
        try
        {
            var stock = new AdaptiveVoxelStock(new StockVolumeConfig(0, 20, 0, 20, 0, 10), 1.0, 0.1);
            stock.InitializeStock();
            AdaptiveVoxelStockState state = stock.ExportState();

            var store = new StockSnapshotStore();
            await store.SaveAsync(temp, state);
            AdaptiveVoxelStockState loaded = await store.LoadAsync(temp);

            Assert.Equal(state.Chunks.Count, loaded.Chunks.Count);
            Assert.Equal(state.Bounds, loaded.Bounds);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
