namespace CNCSS.Vis;

public static class StockVolumeRuntime
{
    public static VoxelStock? TryGetVoxelKernel(IStockVolume? stock) =>
        stock as VoxelStock;
}
