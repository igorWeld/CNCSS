using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic.Voxel;
using CNCSS.UI.Presenters;
using CNCSS.UI.ViewModels;
using CNCSS.Vis;

namespace CNCSS.UI.Hosts
{
    /// <summary>Один шаг playback: рез заготовки + backpressure (SRP вынесен из MainWindow).</summary>
    public sealed class SimulationLoopHost
    {
        private readonly StockRenderService _stockRenderService;

        /// <summary>Создаёт хост с сервисом отрисовки заготовки.</summary>
        public SimulationLoopHost(StockRenderService stockRenderService)
        {
            _stockRenderService = stockRenderService ?? throw new ArgumentNullException(nameof(stockRenderService));
        }

        /// <summary>Применяет шаг реза; возвращает масштаб playback.</summary>
        public SimulationFrameResult ProcessStockCutStep(
            bool isDryRun,
            IStockVolume? stock,
            ToolViewModel? tool,
            bool canRemoveMaterial,
            CutMotionDescriptor motion,
            float deltaMs = 16f) =>
            _stockRenderService.ProcessCutStep(
                isDryRun,
                stock,
                tool,
                canRemoveMaterial,
                motion,
                deltaMs);
    }
}
