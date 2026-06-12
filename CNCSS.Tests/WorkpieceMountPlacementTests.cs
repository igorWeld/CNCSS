using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Machine.Model;
using CNCSS.Vis;

namespace CNCSS.Tests;

public sealed class WorkpieceMountPlacementTests
{
    [Fact]
    public void AlignStockTableLocal_CentersOnMountInTableNodeFrame()
    {
        var def = MachineDefinition.CreateDefault();
        def.WorkpieceMount = new MachineGeometryPoint { X = 10, Y = 20, Z = 5 };
        def.Table.MeshOffset = new MachineGeometryPoint { X = 100, Y = 0, Z = 0 };

        WorkpiecePlacement.StockBounds bounds = WorkpieceMountPlacement.AlignStockTableLocal(
            def,
            tableMeshBoundsLocal: null,
            width: 40,
            depth: 30,
            height: 20);

        Point3D mountNode = WorkpieceMountPlacement.MountLocalToTableNodeFrame(def, def.WorkpieceMount);
        Assert.Equal(mountNode.X, (bounds.MinX + bounds.MaxX) * 0.5, 3);
        Assert.Equal(mountNode.Y, (bounds.MinY + bounds.MaxY) * 0.5, 3);
        Assert.Equal(mountNode.Z + def.FixtureHeightMm, bounds.MinZ, 3);
    }

    [Fact]
    public void ProgramPhysicalToTableLocal_AnchorsAtWcsOriginPlusProgram()
    {
        WorkpieceMountPlacement.ClearWcsOriginTableLocalCache();
        var def = MachineDefinition.CreateDefault();
        var seed = new MachineState();
        def.ApplyHomeToMachineState(seed);
        seed.SetWorkOffset(54, 0, 0, 0);

        var parser = new GCodeParser(seed.Clone());
        parser.ProcessLine("G54 G90 G0 X0 Y0 Z10", 1);

        Point3D wcsOrigin = WorkpieceMountPlacement.GetWcsOriginTableRoot(
            def, parser.State, parser.State.X, parser.State.Y, parser.State.Z);
        Point3D tableLocal = WorkpieceMountPlacement.ProgramPhysicalToTableLocal(
            def,
            parser.State,
            parser.State.X,
            parser.State.Y,
            parser.State.Z);

        Assert.Equal(wcsOrigin.X, tableLocal.X, 3);
        Assert.Equal(wcsOrigin.Y, tableLocal.Y, 3);
        Assert.Equal(wcsOrigin.Z + 10, tableLocal.Z, 3);
    }

    [Fact]
    public void ProgramPhysicalToTableLocal_WithWcsOffset_AddsProgramToWcsAnchor()
    {
        WorkpieceMountPlacement.ClearWcsOriginTableLocalCache();
        var def = MachineDefinition.CreateDefault();
        var seed = new MachineState();
        def.ApplyHomeToMachineState(seed);
        seed.SetWorkOffset(54, 100, 50, 5);

        var parser = new GCodeParser(seed.Clone());
        parser.ProcessLine("G54 G90 G0 X25 Y15 Z0", 1);

        Point3D wcsOrigin = WorkpieceMountPlacement.GetWcsOriginTableRoot(
            def, parser.State, parser.State.X, parser.State.Y, parser.State.Z);
        Point3D tableLocal = WorkpieceMountPlacement.ProgramPhysicalToTableLocal(
            def,
            parser.State,
            parser.State.X,
            parser.State.Y,
            parser.State.Z);

        Assert.Equal(wcsOrigin.X + 25, tableLocal.X, 3);
        Assert.Equal(wcsOrigin.Y + 15, tableLocal.Y, 3);
        Assert.Equal(wcsOrigin.Z, tableLocal.Z, 3);
    }

    [Fact]
    public void GetWcsOriginTableRoot_ProjectsActiveG54McsThroughTableKinematic()
    {
        WorkpieceMountPlacement.ClearWcsOriginTableLocalCache();
        var def = MachineDefinition.CreateDefault();
        var state = new MachineState();
        def.ApplyHomeToMachineState(state);
        state.SetWorkOffset(54, 50, 30, 10);

        Point3D wcsMcs = WorkpieceMountPlacement.GetActiveWorkOffsetMcsPoint(state);
        Point3D onTable = WorkpieceMountPlacement.GetWcsOriginTableRoot(
            def, state, state.X, state.Y, state.Z);
        Matrix3D kin = TableSceneTransforms.BuildTableKinematicMatrix(def, state.X, state.Y, state.Z);
        Point3D backInMcs = kin.Transform(onTable);

        Assert.Equal(wcsMcs.X, backInMcs.X, 2);
        Assert.Equal(wcsMcs.Y, backInMcs.Y, 2);
        Assert.Equal(wcsMcs.Z, backInMcs.Z, 2);
    }

    [Fact]
    public void GetWcsOriginScenePoint_AddsMcsZeroOffset()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { X = 400, Y = 0, Z = 800 };

        Point3D scene = WorkpieceMountPlacement.GetWcsOriginScenePoint(def, -70, -69.653, -460);

        Assert.Equal(330, scene.X, 2);
        Assert.Equal(-69.653, scene.Y, 2);
        Assert.Equal(340, scene.Z, 2);
    }

    [Fact]
    public void PhysicalProgramToTableLocal_SameProgramCoords_SameTableLocalRegardlessOfMachinePose()
    {
        WorkpieceMountPlacement.ClearWcsOriginTableLocalCache();
        var def = MachineDefinition.CreateDefault();
        var seed = new MachineState();
        def.ApplyHomeToMachineState(seed);
        seed.SetWorkOffset(54, 0, 0, 0);

        WorkpieceMountPlacement.SyncWcsOriginTableLocalFromMcs(
            def,
            new Dictionary<int, (double X, double Y, double Z)> { [54] = (0, 0, 0) },
            seed.X,
            seed.Y,
            seed.Z);

        var parserA = new GCodeParser(seed.Clone());
        parserA.ProcessLine("G54 G90 G0 X10 Y20 Z5", 1);
        Point3D onTableA = WorkpieceMountPlacement.PhysicalProgramToTableLocal(
            def, parserA.State, parserA.State.X, parserA.State.Y, parserA.State.Z);

        var parserB = new GCodeParser(seed.Clone());
        parserB.State.X += 80;
        parserB.State.Y += 40;
        parserB.ProcessLine("G54 G90 G0 X10 Y20 Z5", 1);
        Point3D onTableB = WorkpieceMountPlacement.PhysicalProgramToTableLocal(
            def, parserB.State, parserB.State.X, parserB.State.Y, parserB.State.Z);

        Assert.Equal(onTableA.X, onTableB.X, 2);
        Assert.Equal(onTableA.Y, onTableB.Y, 2);
        Assert.Equal(onTableA.Z, onTableB.Z, 2);
    }

    [Fact]
    public void ResolveFixedWcsOriginTableLocal_KeepsTableLocalWhenMachinePoseChanges()
    {
        WorkpieceMountPlacement.ClearWcsOriginTableLocalCache();
        var def = MachineDefinition.CreateDefault();
        var state = new MachineState();
        def.ApplyHomeToMachineState(state);

        Point3D first = WorkpieceMountPlacement.ResolveFixedWcsOriginTableLocal(
            def,
            54,
            50,
            30,
            10,
            state.X,
            state.Y,
            state.Z,
            captureIfMissing: true);
        Point3D second = WorkpieceMountPlacement.ResolveFixedWcsOriginTableLocal(
            def,
            54,
            50,
            30,
            10,
            state.X + 120,
            state.Y + 80,
            state.Z,
            captureIfMissing: true);

        Assert.Equal(first.X, second.X, 2);
        Assert.Equal(first.Y, second.Y, 2);
        Assert.Equal(first.Z, second.Z, 2);
    }

    [Fact]
    public void SyncWcsOriginTableLocalFromMcs_RoundTripsThroughTableKinematic()
    {
        WorkpieceMountPlacement.ClearWcsOriginTableLocalCache();
        var def = MachineDefinition.CreateDefault();
        var state = new MachineState();
        def.ApplyHomeToMachineState(state);

        var offsets = new Dictionary<int, (double X, double Y, double Z)>
        {
            [54] = (-70, -69.653, -460)
        };
        WorkpieceMountPlacement.SyncWcsOriginTableLocalFromMcs(
            def,
            offsets,
            state.X,
            state.Y,
            state.Z);

        Point3D onTable = WorkpieceMountPlacement.GetWcsOriginTableRootForSystem(
            def,
            54,
            -70,
            -69.653,
            -460,
            state.X,
            state.Y,
            state.Z);
        Point3D backInMcs = WorkpieceMountPlacement.TableRootLocalToWcsMcs(
            def,
            state.X,
            state.Y,
            state.Z,
            onTable);

        Assert.Equal(-70, backInMcs.X, 2);
        Assert.Equal(-69.653, backInMcs.Y, 2);
        Assert.Equal(-460, backInMcs.Z, 2);
    }
}
