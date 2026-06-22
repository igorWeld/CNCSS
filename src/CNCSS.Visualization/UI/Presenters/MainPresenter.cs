using System.IO;
using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic.ProgramLoading;
using CNCSS.Machine.Model;
using CNCSS.Simulation.Execution;
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

        public ProgramLoadOrchestrator ProgramLoadOrchestrator => _programLoadOrchestrator;

        public PreparedProgramLoad LoadProgram(
            string filePath,
            MachineState seedState,
            MachineDefinition profile,
            double toolStickOutMm)
        {
            try
            {
                PreparedProgramLoad prepared = _programLoadOrchestrator.Load(filePath, seedState, profile, toolStickOutMm);
                _view.BindProgram(prepared.LoadResult);
                _view.ApplyPreparedProgram(prepared);
                _view.SetStatus($"Program loaded: {Path.GetFileName(prepared.LoadResult.FullPath)}");
                return prepared;
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
