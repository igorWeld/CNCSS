using System.Windows.Media.Media3D;
using CNCSS.Logic;
using CNCSS.Machine.Core;
using CNCSS.Simulation.Bus;
using CNCSS.Simulation.Execution;
using CNCSS.UI.Hosts;
using CNCSS.Vis;

namespace CNCSS.Tests;

public sealed class ProgramLoadOrchestratorTests
{
    [Fact]
    public void Load_ResetsExecutionAndBuildsScene()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cncss-orchestrator-{Guid.NewGuid():N}.nc");
        File.WriteAllLines(path, new[]
        {
            "G90 G0 X0 Y0 Z0",
            "G1 X10 Y0 Z0 F100"
        });

        try
        {
            var bus = new SimulationBus();
            var execution = new ProgramExecutionService(bus);
            var playback = new PlaybackLoopService(execution);
            var cycleCoordinator = new CycleCoordinator(new Controller.Core.ControllerCore(bus), execution, playback);
            var playbackHost = new ProgramPlaybackHost(cycleCoordinator, playback, execution, new MachineCore(bus));
            var orchestrator = new ProgramLoadOrchestrator(
                new ProgramWorkspace(new ProgramLoader(), new ProgramStateService()),
                execution,
                playbackHost,
                new ToolpathSceneBuilder());

            ToolpathSceneBuildResult scene = orchestrator.Load(path);

            Assert.Equal(2, scene.LoadResult.Parser.Commands.Count);
            Assert.NotEmpty(scene.SegmentsWithLines);
            Assert.NotNull(scene.Bounds);
            Assert.Contains("Сегментов пути", scene.StatsText);
            Assert.Equal(new Point3D(0, 0, 0), playbackHost.CurrentPosition);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
