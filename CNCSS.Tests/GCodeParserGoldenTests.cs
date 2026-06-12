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

    [Fact]
    public void Parser_G43AppliesLengthOffsetOnSameBlock()
    {
        var parser = new GCodeParser();
        parser.State.SetToolLengthOffsetValue(1, geom: 100);
        parser.ProcessLine("G90 G54 G43 Z5 H1", 1);

        Assert.Equal(5, parser.Commands[0].Z!.Value, precision: 6);
        Assert.Equal(105, parser.State.Z, precision: 6);
        Assert.Equal(43, parser.State.ToolLengthCompensation.Number);
        Assert.Equal(1, parser.State.ToolLengthOffset!.Value);
    }

    [Fact]
    public void Parser_G43ModalAppliesOnFollowingZMove()
    {
        var parser = new GCodeParser();
        parser.State.SetToolLengthOffsetValue(1, geom: 50);
        parser.ProcessLine("G90 G54 G43 H1", 1);
        parser.ProcessLine("G0 Z10", 2);

        Assert.Equal(10, parser.Commands[1].Z!.Value, precision: 6);
        Assert.Equal(60, parser.State.Z, precision: 6);
    }

    [Fact]
    public void Parser_G43WithDifferentHOnMultiAxisBlock()
    {
        var parser = new GCodeParser();
        parser.State.SetToolLengthOffsetValue(1, geom: 100);
        parser.State.SetToolLengthOffsetValue(2, geom: 30);
        parser.ProcessLine("G90 G54 G43 X0 Y0 Z1 H2", 1);

        Assert.Equal(0, parser.State.X, precision: 6);
        Assert.Equal(0, parser.State.Y, precision: 6);
        Assert.Equal(31, parser.State.Z, precision: 6);
        Assert.Equal(2, parser.State.ToolLengthOffset!.Value);
    }

    [Fact]
    public void CommandReplayer_MatchesParserForG43LengthCompensation()
    {
        var parser = new GCodeParser();
        parser.State.SetToolLengthOffsetValue(1, geom: 100);
        parser.ProcessLine("G90 G54 G43 Z5 H1", 1);

        var replayParser = new GCodeParser(parser.State.Clone());
        CommandReplayer.ReplayCommand(replayParser, parser.Commands[0]);
        Assert.Equal(parser.State.Z, replayParser.State.Z, precision: 6);
    }

    [Fact]
    public void Parser_G43Arc_KeepsProgramPlaneRadiusWithLengthCompensation()
    {
        var parser = new GCodeParser();
        parser.State.SetToolLengthOffsetValue(1, geom: 100);
        parser.ProcessLine("G90 G54 G17 G49", 1);
        parser.ProcessLine("G0 X0 Y0 Z0", 2);
        parser.ProcessLine("G43 H1", 3);
        parser.ProcessLine("G1 Z0 F100", 4);
        parser.ProcessLine("G2 X10 Y0 I5 J0", 5);

        ArcGeometry? arc = parser.Commands[4].Arc;
        Assert.NotNull(arc);
        Assert.Equal(5, arc!.Radius, precision: 3);
        Assert.Equal(arc.StartZ, arc.EndZ, precision: 3);
        Assert.Equal(100, arc.StartZ, precision: 3);
    }

    [Fact]
    public void Parser_G43_G91_IncrementsWorkpieceTipZ()
    {
        var parser = new GCodeParser();
        parser.State.SetToolLengthOffsetValue(1, geom: 100);
        parser.ProcessLine("G90 G54 G43 H1", 1);
        parser.ProcessLine("G0 Z0", 2);
        parser.ProcessLine("G91 G1 Z1 F100", 3);

        Assert.Equal(1, parser.Commands[2].Z!.Value, precision: 6);
        Assert.Equal(101, parser.State.Z, precision: 6);
    }

    [Fact]
    public void MachineAxisToWorkpieceTip_G43Synced_ReturnsProgramContourNotSpindleAxis()
    {
        var state = new MachineState();
        state.SetToolLengthOffsetValue(1, geom: 100);
        state.SetToolLengthCompensation(GCodeRegistry.G43);
        state.ToolLengthOffset = 1;
        state.IsMachineZSyncedWithLengthComp = true;

        state.MachineAxisToWorkpieceTip(0, 0, 100, out _, out _, out double tipZ);
        Assert.Equal(0, tipZ, 3);
    }

    [Fact]
    public void Parser_G43_G91_OnSameBlock_UsesIncrementalTipZ()
    {
        var parser = new GCodeParser();
        parser.State.SetToolLengthOffsetValue(1, geom: 100);
        parser.ProcessLine("G90 G54 G0 Z0", 1);
        parser.ProcessLine("G91 G43 Z5 H1", 2);

        Assert.Equal(5, parser.Commands[1].Z!.Value, precision: 6);
        Assert.Equal(105, parser.State.Z, precision: 6);
    }

    [Fact]
    public void Parser_G43Arc_AfterLengthCompWithoutZMove_KeepsXYRadius()
    {
        var parser = new GCodeParser();
        parser.State.SetToolLengthOffsetValue(1, geom: 100);
        parser.ProcessLine("G90 G54 G17 G49", 1);
        parser.ProcessLine("G0 X0 Y0 Z10", 2);
        parser.ProcessLine("Z10 G43 H1", 3);
        parser.ProcessLine("G2 X10 Y0 I5 J0", 4);

        ArcGeometry? arc = parser.Commands[3].Arc;
        Assert.NotNull(arc);
        Assert.Equal(5, arc!.Radius, precision: 3);
        Assert.Equal(arc.StartZ, arc.EndZ, precision: 3);
        double chord = Math.Sqrt(
            Math.Pow(arc.EndX - arc.StartX, 2) +
            Math.Pow(arc.EndY - arc.StartY, 2));
        Assert.InRange(chord, 9.9, 10.1);
    }
}
