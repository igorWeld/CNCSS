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
    public void BuildWithLineNumbers_WithMachineProfile_AnchorsPathAtWcsInScene()
    {
        var def = MachineDefinition.CreateDefault();
        def.Table.MotionAxes = MachineProgramAxisMask.None;
        def.Spindle.MotionAxes = MachineProgramAxisMask.All;
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
        var wcs = parser.State.GetActiveWorkOffset();
        Point3D wcsScene = MachineAttachmentService.GetWcsOriginScene(
            def,
            new MachineGeometryPoint { X = wcs.X, Y = wcs.Y, Z = wcs.Z });

        Assert.Equal(wcsScene.X + 1, end.X, 3);
        Assert.Equal(wcsScene.Y + 2, end.Y, 3);
        Assert.Equal(wcsScene.Z + 3, end.Z, 3);
    }

    [Fact]
    public void BuildWithLineNumbers_TableXySpindleZ_TcpWorldXyConstant_TableLocalXyVaries()
    {
        var def = MachineDefinition.CreateDefault();
        Assert.True(MachineKinematics.UsesTableMountedWorkpiece(def));

        var seed = new MachineState();
        def.ApplyHomeToMachineState(seed);

        var parser = new GCodeParser(seed.Clone());
        parser.ProcessLine("G54 G90 G0 X0 Y0 Z10", 1);
        parser.ProcessLine("G1 X50 Y0 Z0", 2);
        parser.ProcessLine("G1 X50 Y40 Z0", 3);

        var segments = ToolpathBuilder.BuildWithLineNumbers(parser, seed, def);
        Assert.True(segments.Count >= 2);

        Point3D first = segments[0].Segment.Points[^1];
        Point3D third = segments[^1].Segment.Points[^1];

        Point3D tcpHome = KinematicChainSolver.ComputeToolCenterPoint(def, 0, 0, 10);
        Point3D tcpEnd = KinematicChainSolver.ComputeToolCenterPoint(def, parser.State.X, parser.State.Y, parser.State.Z);

        Assert.True(Math.Abs(tcpEnd.X - tcpHome.X) < 1e-3, "World TCP X must stay fixed while table moves in X");
        Assert.True(Math.Abs(tcpEnd.Y - tcpHome.Y) < 1e-3, "World TCP Y must stay fixed while table moves in Y");
        Assert.True(Math.Abs(third.X - first.X) > 40, "Table-local path must move in X");
        Assert.True(Math.Abs(third.Y - first.Y) > 30, "Table-local path must move in Y");
    }

    [Fact]
    public void BuildWithLineNumbers_TableMounted_PathStartsFromWcsOrigin()
    {
        WorkpieceMountPlacement.ClearWcsOriginTableLocalCache();
        var def = MachineDefinition.CreateDefault();

        var seed = new MachineState();
        def.ApplyHomeToMachineState(seed);
        seed.SetWorkOffset(54, 0, 0, 0);

        var parser = new GCodeParser(seed.Clone());
        parser.ProcessLine("G54 G90 G0 X0 Y0 Z10", 1);

        var segments = ToolpathBuilder.BuildWithLineNumbers(parser, seed, def);
        Assert.NotEmpty(segments);
        Point3D atProgram = segments[0].Segment.Points[^1];
        (double refX, double refY, double refZ) = WorkpieceMountPlacement.GetProgramZeroPhysical(parser.State);
        Point3D wcsOrigin = WorkpieceMountPlacement.GetWcsOriginTableRoot(
            def, parser.State, refX, refY, refZ);

        Assert.Equal(wcsOrigin.X, atProgram.X, 3);
        Assert.Equal(wcsOrigin.Y, atProgram.Y, 3);
        Assert.Equal(wcsOrigin.Z + 10, atProgram.Z, 3);
    }

    [Fact]
    public void BuildWithLineNumbers_TableMounted_ProgramOffsetFromWcsAnchor()
    {
        WorkpieceMountPlacement.ClearWcsOriginTableLocalCache();
        var def = MachineDefinition.CreateDefault();
        def.WorkpieceMount = MachineGeometryPoint.Zero;

        var seed = new MachineState();
        def.ApplyHomeToMachineState(seed);
        seed.SetWorkOffset(54, 0, 0, 0);

        var parser = new GCodeParser(seed.Clone());
        parser.ProcessLine("G54 G90 G0 X0 Y0 Z0", 1);
        parser.ProcessLine("G1 X25 Y15 Z0", 2);

        var segments = ToolpathBuilder.BuildWithLineNumbers(parser, seed, def);
        (double refX, double refY, double refZ) = WorkpieceMountPlacement.GetProgramZeroPhysical(parser.State);
        Point3D wcsOrigin = WorkpieceMountPlacement.GetWcsOriginTableRoot(
            def, parser.State, refX, refY, refZ);
        Point3D end = segments[^1].Segment.Points[^1];

        Assert.Equal(wcsOrigin.X + 25, end.X, 2);
        Assert.Equal(wcsOrigin.Y + 15, end.Y, 2);
    }
}
