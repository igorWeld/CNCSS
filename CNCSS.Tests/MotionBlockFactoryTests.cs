using CNCSS.Logic;
using CNCSS.Machine.Model;
using CNCSS.Simulation.Execution;

namespace CNCSS.Tests;

public sealed class MotionBlockFactoryTests
{
    [Theory]
    [InlineData("G0 X1", MotionBlockKind.Rapid)]
    [InlineData("G1 X1 F100", MotionBlockKind.Linear)]
    [InlineData("G2 X10 Y0 I5 J0", MotionBlockKind.Arc)]
    [InlineData("G4 P1", MotionBlockKind.Dwell)]
    [InlineData("T1 M6", MotionBlockKind.ToolChange)]
    public void FromParsedCommand_ClassifiesCanonicalBlockKind(string line, MotionBlockKind expectedKind)
    {
        var parser = new GCodeParser();
        parser.ProcessLine(line, 1);

        var block = MotionBlockFactory.FromParsedCommand(parser.Commands[0]);

        Assert.Equal(expectedKind, block.Kind);
    }

    [Fact]
    public void FromParsedCommand_UsesModalMotionModeForCoordinateOnlyLines()
    {
        var parser = new GCodeParser();
        parser.ProcessLine("G1 X10 F100", 1);
        parser.ProcessLine("X20 F100", 2);

        var modalLinear = MotionBlockFactory.FromParsedCommand(parser.Commands[1]);

        Assert.Equal(MotionBlockKind.Linear, modalLinear.Kind);
    }

    [Fact]
    public void FromParsedCommand_CapturesStopAndEndMetadata()
    {
        var parser = new GCodeParser();
        parser.ProcessLine("M1", 1);
        parser.ProcessLine("M30", 2);

        var optionalStop = MotionBlockFactory.FromParsedCommand(parser.Commands[0]);
        var programEnd = MotionBlockFactory.FromParsedCommand(parser.Commands[1]);

        Assert.True(optionalStop.IsOptionalStop);
        Assert.True(programEnd.IsProgramEnd);
        Assert.Equal(30, programEnd.EndProgramMCode);
    }
}
