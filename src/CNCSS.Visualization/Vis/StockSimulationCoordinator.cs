using System.Windows.Media;
using CNCSS.Data;
using CNCSS.Data.Config;

namespace CNCSS.Vis
{
    /// <summary>Создаёт заготовку для симуляции на новом адаптивном движке.</summary>
    public sealed class StockSimulationCoordinator
    {
        public StockRuntime CreateRuntime(
            StockVolumeConfig cfg,
            double resolutionMm,
            Color? defaultStockColor = null,
            StockConstructorConfig? constructorConfig = null,
            double displayGridMm = VoxelConstants.DisplayGridDefaultMm)
        {
            var stock = VoxelStockVolume.Create(cfg, constructorConfig, defaultStockColor, resolutionMm, displayGridMm);
            return new StockRuntime(stock, resolutionMm);
        }

        public VoxelStockVolume CreateHost(
            StockVolumeConfig cfg,
            StockConstructorConfig? constructorConfig,
            double displayGridMm = VoxelConstants.DisplayGridDefaultMm) =>
            VoxelStockVolume.Create(cfg, constructorConfig, resolutionMm: VoxelConstants.VoxelResolutionMm, displayGridMm: displayGridMm);

        public IStockVolume WrapHost(
            VoxelStockVolume host,
            Color? defaultStockColor,
            StockConstructorConfig? constructorConfig,
            StockVolumeConfig? volume = null) =>
            host;
    }

    public sealed record StockRuntime(IStockVolume Stock, double ResolutionMm);
}
