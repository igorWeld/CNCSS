using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Simulation.Bus;
using CNCSS.Simulation.Execution;

namespace CNCSS.Tests;

public sealed class PlaybackLoopServiceTests
{
    [Fact]
    public void Tick_AfterFeedHoldResume_ContinuesCurrentSegment()
    {
        var parser = new GCodeParser();
        parser.ProcessLine("G90 G0 X255 Y255 Z255", 1);
        parser.ProcessLine("G1 X355 Y255 Z255 F600", 2);

        var execution = new ProgramExecutionService(new SimulationBus());
        execution.LoadProgram(parser.Commands);
        execution.SetCurrentIndex(0);

        var playback = new PlaybackLoopService(execution);
        var home = new Point3D(0, 0, 0);
        playback.Reset(home);
        playback.BeginSegmentFrom(home);

        var firstTick = playback.Tick(
            machineIsRunning: true,
            lastPosition: home,
            currentLineIndex: 0,
            speedResolver: _ => 10,
            simulationMultiplier: 1,
            fpsSlowdownFactor: 1);

        Assert.True(playback.IsSegmentInProgress);
        Assert.True(firstTick.CurrentPosition.X > home.X);
        Assert.True(firstTick.CurrentPosition.X < 355);

        var resumedTick = playback.Tick(
            machineIsRunning: true,
            lastPosition: firstTick.CurrentPosition,
            currentLineIndex: 1,
            speedResolver: _ => 10,
            simulationMultiplier: 1,
            fpsSlowdownFactor: 1);

        Assert.NotEqual(PlaybackLoopAction.StopProgram, resumedTick.Action);
        Assert.True(resumedTick.CurrentPosition.X > firstTick.CurrentPosition.X);
        Assert.True(resumedTick.CurrentPosition.X < 355);
    }
}
