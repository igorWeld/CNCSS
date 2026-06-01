using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Simulation.Bus;
using CNCSS.Simulation.Execution;
using CNCSS.Controller.Core;

namespace CNCSS.Tests;

public sealed class CycleCoordinatorTests
{
    [Fact]
    public void TryStart_DoesNotResetInProgressPlaybackSegment()
    {
        var bus = new SimulationBus();
        var controller = new ControllerCore(bus);
        var execution = new ProgramExecutionService(bus);
        var playback = new PlaybackLoopService(execution);
        var coordinator = new CycleCoordinator(controller, execution, playback);
        var parser = new GCodeParser();
        parser.ProcessLine("G90 G0 X255 Y255 Z255", 1);
        parser.ProcessLine("G1 X355 Y255 Z255 F600", 2);
        execution.LoadProgram(parser.Commands);
        playback.Reset(new Point3D(0, 0, 0));

        Assert.True(coordinator.TryStart(0, 2, new Point3D(0, 0, 0), () => true, out _));
        var tick = playback.Tick(true, new Point3D(0, 0, 0), 0, _ => 10, 1, 1);
        Assert.True(coordinator.TryFeedHold());

        Assert.True(coordinator.TryStart(1, 2, tick.CurrentPosition, () => true, out _));

        Assert.True(playback.IsSegmentInProgress);
    }
}
