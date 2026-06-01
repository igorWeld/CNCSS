using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Machine.Model;
using CNCSS.Vis;

namespace CNCSS.Tests;

public sealed class ToolpathBuilderTests
{
    [Fact]
    public void BuildWithLineNumbers_IncludesWorkOffsetAndMcsZeroInSceneCoordinates()
    {
        var seed = new MachineState
        {
            MachineZeroOffsetX = 100,
            MachineZeroOffsetY = 50,
            MachineZeroOffsetZ = 200
        };
        seed.SetWorkOffset(54, 10, 20, 30);

        var parser = new GCodeParser(seed.Clone());
        parser.ProcessLine("G54 G90 G0 X1 Y2 Z3", 1);

        var segments = ToolpathBuilder.BuildWithLineNumbers(parser, seed);
        Assert.NotEmpty(segments);
        Point3D end = segments[0].Segment.Points[^1];

        Assert.Equal(111, end.X, 3);
        Assert.Equal(72, end.Y, 3);
        Assert.Equal(233, end.Z, 3);
    }

    [Fact]
    public void BuildWithLineNumbers_WithMachineProfile_UsesTcpNotRawAxisPose()
    {
        var def = MachineDefinition.CreateDefault();
        def.Axes.First(a => a.Name == "Z").Home = 40;
        def.SyncHomePositionFromAxes();
        def.ToolMount = new MachineGeometryPoint { Z = 25 };

        var seed = new MachineState
        {
            MachineZeroOffsetX = 100,
            MachineZeroOffsetY = 50,
            MachineZeroOffsetZ = 200
        };
        seed.SetWorkOffset(54, 10, 20, 30);

        var parser = new GCodeParser(seed.Clone());
        parser.ProcessLine("G54 G90 G0 X1 Y2 Z3", 1);

        var segments = ToolpathBuilder.BuildWithLineNumbers(parser, seed, def);
        Point3D end = segments[0].Segment.Points[^1];
        Point3D expectedTcp = KinematicChainSolver.ComputeToolCenterPoint(def, parser.State.X, parser.State.Y, parser.State.Z);

        Assert.Equal(expectedTcp.X, end.X, 3);
        Assert.Equal(expectedTcp.Y, end.Y, 3);
        Assert.Equal(expectedTcp.Z, end.Z, 3);
        Assert.NotEqual(parser.State.Z, end.Z, 3);
    }
}
