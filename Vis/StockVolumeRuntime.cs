namespace CNCSS.Vis;

/// <summary>Вспомогательные проверки типа runtime-заготовки (воксельное ядро).</summary>
public static class StockVolumeRuntime
{
    public static VoxelStock? TryGetVoxelKernel(IStockVolume? stock) =>
        stock as VoxelStock;
}
