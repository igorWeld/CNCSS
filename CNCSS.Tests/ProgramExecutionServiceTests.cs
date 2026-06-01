using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Simulation.Bus;
using CNCSS.Simulation.Execution;

namespace CNCSS.Tests;

public sealed class ProgramExecutionServiceTests
{
    [Fact]
    public void GetNextMoveDecision_StopsAtOptionalBlockWhenEnabled()
    {
        var parser = new GCodeParser();
        parser.ProcessLine("G90 G0 X255 Y255 Z255", 1);
        parser.ProcessLine("M1", 2);
        parser.ProcessLine("G1 X300 F600", 3);

        var execution = new ProgramExecutionService(new SimulationBus());
        execution.LoadProgram(parser.Commands);
        execution.SetCurrentIndex(0);
        execution.SetOptionalStop(true);

        ProgramMoveDecision decision = execution.GetNextMoveDecision(0, 0, 0);

        Assert.True(decision.StopForOptional);
        Assert.False(decision.HasMove);
        Assert.Equal(1, decision.NextIndex);
    }

    [Fact]
    public void ShouldHoldForSingleBlock_WhenFirstBlockFinishes_ReturnsTrue()
    {
        var parser = new GCodeParser();
        parser.ProcessLine("G90 G0 X255 Y255 Z255", 1);
        parser.ProcessLine("G1 X300 F600", 2);

        var execution = new ProgramExecutionService(new SimulationBus());
        execution.LoadProgram(parser.Commands);
        execution.SetCurrentIndex(0);
        execution.SetSingleBlock(true);
        execution.BeginCycle();

        ProgramMoveDecision decision = execution.GetNextMoveDecision(0, 0, 0);

        Assert.True(decision.HasMove);
        Assert.True(execution.ShouldHoldForSingleBlock(1.0));
    }

    [Fact]
    public void GetNextMoveDecision_G28ReturnsHomeInTwoStagesWhenZNotHome()
    {
        var parser = new GCodeParser();
        parser.ProcessLine("G90 G0 X255 Y255 Z255", 1);
        parser.ProcessLine("G28 X10 Y20 Z30", 2);

        var execution = new ProgramExecutionService(new SimulationBus());
        execution.LoadProgram(parser.Commands);
        execution.SetCurrentIndex(0);

        ProgramMoveDecision stage1 = execution.GetNextMoveDecision(10, 20, 30);
        ProgramMoveDecision stage2 = execution.GetNextMoveDecision(10, 20, 0);

        Assert.True(stage1.HasMove);
        Assert.Equal(10, stage1.TargetX, precision: 6);
        Assert.Equal(20, stage1.TargetY, precision: 6);
        Assert.Equal(0, stage1.TargetZ, precision: 6);

        Assert.True(stage2.HasMove);
        Assert.Equal(0, stage2.TargetX, precision: 6);
        Assert.Equal(0, stage2.TargetY, precision: 6);
        Assert.Equal(0, stage2.TargetZ, precision: 6);
    }

    [Fact]
    public void GetNextMoveDecision_M30EndsProgramAndRequestsRewind()
    {
        var parser = new GCodeParser();
        parser.ProcessLine("G90 G0 X255 Y255 Z255", 1);
        parser.ProcessLine("M30", 2);

        var execution = new ProgramExecutionService(new SimulationBus());
        execution.LoadProgram(parser.Commands);
        execution.SetCurrentIndex(0);

        execution.GetNextMoveDecision(0, 0, 0);
        ProgramMoveDecision end = execution.GetNextMoveDecision(0, 0, 0);

        Assert.True(end.EndOfProgram);
        Assert.Equal(30, end.EndProgramMCode);
        Assert.True(end.RewindToStart);
    }
}
