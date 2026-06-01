using CNCSS.Data;
using CNCSS.Logic;

namespace CNCSS.Tests;

public sealed class GCodeParserGoldenTests
{
    [Fact]
    public void Parser_AppliesAbsoluteAndIncrementalLinearMoves()
    {
        var parser = new GCodeParser();

        parser.ProcessLine("G90 G0 X10 Y20 Z5", 1);
        parser.ProcessLine("G91 G1 X2 Y-3 Z-1 F120", 2);

        Assert.Equal(12, parser.State.X, precision: 6);
        Assert.Equal(17, parser.State.Y, precision: 6);
        Assert.Equal(4, parser.State.Z, precision: 6);
        Assert.Equal(120, parser.State.FeedRate, precision: 6);
        Assert.False(parser.State.IsAbsolute);
    }

    [Fact]
    public void Parser_AppliesWorkOffsetToAbsoluteProgramCoordinates()
    {
        var parser = new GCodeParser();

        parser.ProcessLine("G10 L2 P1 X10 Y20 Z30", 1);
        parser.ProcessLine("G54 G90 G0 X1 Y2 Z3", 2);

        Assert.Equal(11, parser.State.X, precision: 6);
        Assert.Equal(22, parser.State.Y, precision: 6);
        Assert.Equal(33, parser.State.Z, precision: 6);
        Assert.Equal(54, parser.State.CurrentCoordinateSystem.Number);
    }

    [Fact]
    public void Parser_CreatesArcGeometryForIjkAndRadiusArcs()
    {
        var parser = new GCodeParser();

        parser.ProcessLine("G90 G17 G0 X0 Y0 Z0", 1);
        parser.ProcessLine("G2 X10 Y0 I5 J0", 2);
        parser.ProcessLine("G3 X0 Y0 R5", 3);

        Assert.NotNull(parser.Commands[1].Arc);
        Assert.NotNull(parser.Commands[2].Arc);
        Assert.Equal(17, parser.Commands[1].Arc!.Plane);
    }

    [Fact]
    public void Parser_TracksToolSpindleCoolantAndProgramEndCodes()
    {
        var parser = new GCodeParser();

        parser.ProcessLine("T7 M6", 1);
        parser.ProcessLine("S2500 M3 M8", 2);
        parser.ProcessLine("M30", 3);

        Assert.Equal(7, parser.State.ToolNumber);
        Assert.Equal(2500, parser.State.SpindleSpeed, precision: 6);
        Assert.True(parser.State.IsSpindleOn);
        Assert.True(parser.State.IsCoolantOn);
        Assert.Contains(parser.Commands.Last().MCodes, m => m.Number == 30);
    }

    [Fact]
    public void CommandReplayer_RebuildsStateAtSelectedLine()
    {
        var parser = new GCodeParser();
        parser.ProcessLine("G90 G0 X10", 1);
        parser.ProcessLine("G91 G1 X5", 2);
        parser.ProcessLine("G90 G0 X100", 3);

        MachineState stateAfterSecondLine = CommandReplayer.StateAfterCommand(parser, 1);

        Assert.Equal(15, stateAfterSecondLine.X, precision: 6);
        Assert.False(stateAfterSecondLine.IsAbsolute);
    }
}
