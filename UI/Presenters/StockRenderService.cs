using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media.Media3D;
using CNCSS.UI.ViewModels;
using CNCSS.Vis;
using CNCSS.GpuVerification;

namespace CNCSS.UI.Presenters
{
    /// <summary>Обёртка над шагом съёма заготовки и политикой частоты обновления отображения вокселей.</summary>
    public sealed class StockRenderService
    {
        private DateTime _lastStockUpdateTime = DateTime.MinValue;

        public void ProcessCutStep(
            bool isDryRunEnabled,
            IStockVolume? stock,
            StockCutWorker? stockCutWorker,
            ToolViewModel? selectedTool,
            Point3D from,
            Point3D to,
            GpuOccupancySession? gpuOccupancyProgramSession = null)
        {
            if (isDryRunEnabled || stock == null || selectedTool == null)
            {
                return;
            }

            double radius = selectedTool.Diameter / 2.0;
            if (stock is VoxelStock)
            {
                stock.CutCylinder(from, to, radius, selectedTool.FluteLength, selectedTool.FluteColor);
                if (gpuOccupancyProgramSession != null)
                {
                    gpuOccupancyProgramSession.ApplyProgramCut(new GpuCylinderCut(
                        from.X,
                        from.Y,
                        from.Z,
                        to.X,
                        to.Y,
                        to.Z,
                        radius,
                        selectedTool.FluteLength));
                }

                return;
            }

            stockCutWorker?.EnqueueCut(from, to, radius, selectedTool.FluteLength, selectedTool.FluteColor);
        }

        /// <summary>Нужно ли пересобрать меш заготовки (очередь обновлений сериализуется снаружи — здесь только троттлинг по времени).</summary>
        public bool ShouldRefreshStock(IStockVolume? stock, int minRefreshIntervalMs)
        {
            if (stock == null || !stock.IsDirty)
            {
                return false;
            }

            return (DateTime.Now - _lastStockUpdateTime).TotalMilliseconds > minRefreshIntervalMs;
        }

        public async Task RefreshStockVisualAsync(
            IStockVolume? stock,
            ModelVisual3D? stockVisual,
            Border? stockProgressPanel,
            bool showProgress,
            bool stockShownInViewport)
        {
            if (stock == null || stockVisual == null)
            {
                return;
            }

            if (showProgress && stockProgressPanel != null)
            {
                stockProgressPanel.Visibility = Visibility.Visible;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            }

            if (stock is VoxelStock voxelStock)
            {
                await voxelStock.UpdateAllVisualsAsync();
            }
            else
            {
                await stock.UpdateVisualsAsync();
            }

            if (Application.Current?.Dispatcher.CheckAccess() == true)
            {
                await Dispatcher.Yield(DispatcherPriority.Render);
            }

            if (stockShownInViewport)
            {
                if (stockVisual.Content != stock.MainModel)
                {
                    stockVisual.Content = stock.MainModel;
                }
            }
            else
            {
                stockVisual.Content = null;
            }

            // Всегда двигаем время: иначе при повторных запросах интервал не обновляется
            // и меш запрашивается почти каждый тик анимации.
            _lastStockUpdateTime = DateTime.Now;
            if (stockProgressPanel != null)
            {
                stockProgressPanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
