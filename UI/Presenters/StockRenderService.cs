using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using CNCSS.UI.ViewModels;
using CNCSS.Vis;

namespace CNCSS.UI.Presenters
{
    /// <summary>Обёртка над шагом съёма заготовки и политикой частоты обновления отображения вокселей.</summary>
    public sealed class StockRenderService
    {
        private DateTime _lastStockUpdateTime = DateTime.MinValue;

        public void ProcessCutStep(
            bool isDryRunEnabled,
            VoxelStock? stock,
            StockCutWorker? stockCutWorker,
            CheckBox stockVisibleCheck,
            ToolViewModel? selectedTool,
            Point3D from,
            Point3D to)
        {
            if (isDryRunEnabled || stock == null || stockVisibleCheck.IsChecked != true || selectedTool == null)
            {
                return;
            }

            stockCutWorker?.EnqueueCut(from, to, selectedTool.Diameter / 2.0, selectedTool.FluteLength, selectedTool.FluteColor);
        }

        public bool ShouldRefreshStock(VoxelStock? stock, bool isStockUpdating, int minRefreshIntervalMs)
        {
            if (stock == null || isStockUpdating || !stock.IsDirty)
            {
                return false;
            }

            return (DateTime.Now - _lastStockUpdateTime).TotalMilliseconds > minRefreshIntervalMs;
        }

        public async Task RefreshStockVisualAsync(
            VoxelStock stock,
            ModelVisual3D stockVisual,
            Border? stockProgressPanel,
            bool showProgress)
        {
            if (showProgress && stockProgressPanel != null)
            {
                stockProgressPanel.Visibility = Visibility.Visible;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            }

            await stock.UpdateVisualsAsync();
            if (stockVisual.Content != stock.MainModel)
            {
                stockVisual.Content = stock.MainModel;
            }

            _lastStockUpdateTime = DateTime.Now;
            if (stockProgressPanel != null)
            {
                stockProgressPanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
