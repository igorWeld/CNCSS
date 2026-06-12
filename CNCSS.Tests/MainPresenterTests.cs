using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Logic.NcPrograms;
using CNCSS.Logic.ProgramLoading;
using CNCSS.Simulation.Bus;
using CNCSS.Simulation.Execution;
using CNCSS.Machine.Core;
using CNCSS.Machine.Model;
using CNCSS.UI.Presenters;
using CNCSS.UI.Views;
using CNCSS.Controller.Core;
using CNCSS.UI.Hosts;

namespace CNCSS.Tests;

public sealed class MainPresenterTests
{
    [Fact]
    public void LoadProgram_UpdatesViewThroughMvpContract()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cncss-presenter-{Guid.NewGuid():N}.nc");
        File.WriteAllLines(path, new[] { "G90 G0 X5" });
        var view = new FakeMainView();
        var presenter = CreatePresenter(view);

        try
        {
            presenter.LoadProgram(path, new MachineState(), new MachineDefinition(), toolStickOutMm: 0);

            Assert.Single(view.BoundLines);
            Assert.Contains("Program loaded:", view.Status);
            Assert.Contains("Сегментов пути", view.Stats);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reset_DelegatesCycleStateAndUpdatesButtons()
    {
        var view = new FakeMainView();
        var presenter = CreatePresenter(view);

        presenter.Reset(new Point3D(0, 0, 0));

        Assert.Equal(0, view.SelectedLine);
        Assert.True(view.CanStart);
        Assert.False(view.CanPause);
    }

    private static MainPresenter CreatePresenter(IMainView view)
    {
        var bus = new SimulationBus();
        var controller = new ControllerCore(bus);
        var execution = new ProgramExecutionService(bus);
        var playback = new PlaybackLoopService(execution);
        var cycleCoordinator = new CycleCoordinator(controller, execution, playback);
        var playbackHost = new ProgramPlaybackHost(cycleCoordinator, playback, execution, new MachineCore(bus));
        var orchestrator = new ProgramLoadOrchestrator(
            new ProgramWorkspace(new ProgramLoader(), new ProgramStateService()),
            execution,
            playbackHost);
        return new MainPresenter(
            view,
            orchestrator,
            cycleCoordinator);
    }

    private sealed class FakeMainView : IMainView
    {
        public string[] BoundLines { get; private set; } = Array.Empty<string>();
        public string Status { get; private set; } = string.Empty;
        public string Stats { get; private set; } = string.Empty;
        public int SelectedLine { get; private set; } = -1;
        public bool CanStart { get; private set; }
        public bool CanPause { get; private set; }

        public void BindProgram(ProgramLoadResult loadResult) => BoundLines = loadResult.Lines;
        public void ApplyPreparedProgram(PreparedProgramLoad prepared) => Stats = prepared.StatsText;
        public void ClearProgramView() => BoundLines = Array.Empty<string>();
        public void SelectProgramLine(int index) => SelectedLine = index;
        public void ApplyLineState(MachineState state) { }
        public void SetCycleButtons(bool canStart, bool canPause)
        {
            CanStart = canStart;
            CanPause = canPause;
        }
        public void ShowError(string message) => Status = message;
        public void SetStatus(string message) => Status = message;
    }
}
