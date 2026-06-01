using System.IO;
using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic.ProgramLoading;
using CNCSS.Simulation.Execution;
using CNCSS.Vis;
using CNCSS.UI.Views;

namespace CNCSS.UI.Presenters
{
    /// <summary>
    /// Презентер для главного окна.
    /// Управляет логикой взаимодействия между моделью (парсером) и представлением.
    /// </summary>
    public sealed class MainPresenter
    {
        private readonly IMainView _view;
        private readonly CycleCoordinator _cycleCoordinator;
        private readonly ProgramLoadOrchestrator _programLoadOrchestrator;

        public MainPresenter(
            IMainView view,
            ProgramLoadOrchestrator programLoadOrchestrator,
            CycleCoordinator cycleCoordinator)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _programLoadOrchestrator = programLoadOrchestrator ?? throw new ArgumentNullException(nameof(programLoadOrchestrator));
            _cycleCoordinator = cycleCoordinator ?? throw new ArgumentNullException(nameof(cycleCoordinator));
        }

        public ToolpathSceneBuildResult LoadProgram(string filePath)
        {
            try
            {
                var result = _programLoadOrchestrator.Load(filePath);
                _view.BindProgram(result.LoadResult);
                _view.SetStatus($"Program loaded: {Path.GetFileName(result.LoadResult.FullPath)}");
                return result;
            }
            catch (Exception ex)
            {
                _view.ShowError($"Ошибка при загрузке файла: {ex.Message}");
                throw;
            }
        }

        public void OnLineSelected(int index)
        {
            var state = _programLoadOrchestrator.Workspace.GetStateAtUiLine(index + 1);
            if (state != null)
            {
                _view.ApplyLineState(state);
            }
        }

        public void Reset(Point3D homePosition)
        {
            _cycleCoordinator.Reset(homePosition);
            _view.SelectProgramLine(0);
            _view.SetCycleButtons(canStart: true, canPause: false);
        }

        public void ClearProgram()
        {
            _programLoadOrchestrator.Workspace.Clear();
            _view.ClearProgramView();
        }
    }
}
