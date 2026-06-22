using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media.Media3D;
using CNCSS.Logic.Voxel;
using CNCSS.UI.ViewModels;
using CNCSS.Vis;

namespace CNCSS.UI.Presenters
{
    /// <summary>Шаг съёма заготовки с синхронизацией cut через <see cref="CutVisualSynchronizer"/>.</summary>
    public sealed class StockRenderService
    {
        private readonly CutVisualSynchronizer _synchronizer = new();

        public SimulationFrameResult ProcessCutStep(
            bool isDryRunEnabled,
            IStockVolume? stock,
            ToolViewModel? selectedTool,
            bool canRemoveMaterial,
            CutMotionDescriptor motion,
            float deltaMs = 16f)
        {
            if (selectedTool == null)
            {
                return new SimulationFrameResult(true, 0, 1.0);
            }

            FluteCutProfile profile = CutVisualSynchronizer.BuildProfile(selectedTool);
            return _synchronizer.ProcessFrame(
                stock,
                isDryRunEnabled,
                canRemoveMaterial,
                motion,
                profile,
                selectedTool.FluteColor,
                deltaMs);
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
                await Dispatcher.Yield(DispatcherPriority.Render);
            }

            await stock.UpdateVisualsAsync();

            if (stockShownInViewport)
            {
                if (stock is VoxelStockVolume { ShellDisplayed: true })
                {
                    // SharpDX overlay renders voxel shell; parametric root may be empty.
                }
                else
                {
                    Model3D content = stock.MainModel;
                    if (stockVisual.Content != content)
                    {
                        stockVisual.Content = content;
                    }
                }
            }
            else
            {
                stockVisual.Content = null;
            }

            if (stockProgressPanel != null)
            {
                stockProgressPanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
