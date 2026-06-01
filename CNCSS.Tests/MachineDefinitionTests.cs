using System.Windows.Media.Media3D;
using CNCSS.Logic;
using CNCSS.Machine.Configuration;
using CNCSS.Machine.Model;

namespace CNCSS.Tests;

public sealed class MachineDefinitionTests
{
    [Fact]
    public void CreateDefault_HasExpectedAxisLimitsTcpAndMcsHome()
    {
        var def = MachineDefinition.CreateDefault();

        var x = def.Axes.First(a => a.Name == "X");
        var y = def.Axes.First(a => a.Name == "Y");
        var z = def.Axes.First(a => a.Name == "Z");

        Assert.Equal(-300, x.Min, 3);
        Assert.Equal(300, x.Max, 3);
        Assert.Equal(-200, y.Min, 3);
        Assert.Equal(200, y.Max, 3);
        Assert.Equal(-500, z.Min, 3);
        Assert.Equal(0, z.Max, 3);
        Assert.Equal(0, x.Home, 3);
        Assert.Equal(0, y.Home, 3);
        Assert.Equal(0, z.Home, 3);
        Assert.Equal(0, def.ToolMount.X, 3);
        Assert.Equal(0, def.ToolMount.Y, 3);
        Assert.Equal(0, def.ToolMount.Z, 3);
        Assert.True(def.McsZeroOffset.IsNearlyZero());
    }

    [Fact]
    public void MachineProfileStore_DeleteAllProfiles_RemovesKnownProfile()
    {
        string root = Path.Combine(Path.GetTempPath(), "cncss_profiles_" + Guid.NewGuid().ToString("N"));
        var store = new MachineProfileStore(root);
        store.Save(MachineDefinition.CreateDefault("test_a", "A"));
        Assert.Single(store.ListProfileIds());

        store.DeleteAllProfiles();
        Assert.Empty(store.ListProfileIds());
        Assert.False(File.Exists(store.GetActiveProfilePointerPath()));

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void Validate_RejectsInvalidAxisRange()
    {
        var def = MachineDefinition.CreateDefault();
        def.Axes[0].Min = 100;
        def.Axes[0].Max = 10;

        IReadOnlyList<string> errors = def.Validate();
        Assert.Contains(errors, e => e.Contains("X"));
    }

    [Fact]
    public void KinematicChainSolver_ReturnsToolPoint()
    {
        var def = MachineDefinition.CreateDefault();
        var pose = KinematicChainSolver.Solve(def, 100, 120, 50);

        Assert.Equal(100, pose.X);
        Assert.Equal(120, pose.Y);
        Assert.Equal(50, pose.Z);
        Assert.True(double.IsFinite(pose.ToolCenterPoint.X));
    }

    [Fact]
    public void KinematicChainSolver_SpindleMovesUpWhenPhysicalZIncreases()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { Z = 200 };
        def.HomePosition = new MachineGeometryPoint { Z = 0 };

        double spindleZAtLow = GetSpindleOffsetZ(KinematicChainSolver.SolveTransforms(def, 0, 0, 100));
        double spindleZAtHigh = GetSpindleOffsetZ(KinematicChainSolver.SolveTransforms(def, 0, 0, 250));

        Assert.True(spindleZAtHigh > spindleZAtLow);
    }

    [Fact]
    public void MotionCouplingPoint_MovesWithAxisTravel_NotFixedParentAttach()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { Z = 200 };
        def.SyncHomePositionFromAxes();
        double homePhysicalZ = def.GetPhysicalHomePosition().Z;

        IReadOnlyDictionary<string, Transform3D> atHome =
            KinematicChainSolver.SolveTransforms(def, 0, 0, homePhysicalZ);
        IReadOnlyDictionary<string, Transform3D> lower =
            KinematicChainSolver.SolveTransforms(def, 0, 0, homePhysicalZ - 50);
        IReadOnlyDictionary<string, Transform3D> higher =
            KinematicChainSolver.SolveTransforms(def, 0, 0, homePhysicalZ + 50);

        Point3D fixedParentAttach = new(
            def.Spindle.AttachOnParent.X,
            def.Spindle.AttachOnParent.Y,
            def.Spindle.AttachOnParent.Z);

        Point3D couplingHome = MachineNodeMeshTransforms.GetMotionCouplingPoint(
            atHome[MachineNodeIds.Spindle],
            def.Spindle.AttachOnChild);
        Point3D couplingLower = MachineNodeMeshTransforms.GetMotionCouplingPoint(
            lower[MachineNodeIds.Spindle],
            def.Spindle.AttachOnChild);
        Point3D couplingHigher = MachineNodeMeshTransforms.GetMotionCouplingPoint(
            higher[MachineNodeIds.Spindle],
            def.Spindle.AttachOnChild);

        Assert.Equal(100, couplingHigher.Z - couplingLower.Z, 3);
        Assert.Equal(fixedParentAttach.Z, couplingHome.Z, 3);
        Assert.NotEqual(fixedParentAttach.Z, couplingLower.Z, 3);
        Assert.NotEqual(fixedParentAttach.Z, couplingHigher.Z, 3);
    }

    [Fact]
    public void GetPhysicalHomePosition_AddsMcsZeroOffset()
    {
        var def = MachineDefinition.CreateDefault();
        def.Axes.First(a => a.Name == "Z").Home = 10;
        def.McsZeroOffset = new MachineGeometryPoint { Z = 200 };
        def.SyncHomePositionFromAxes();

        Assert.Equal(210, def.GetPhysicalHomePosition().Z, 3);
    }

    [Fact]
    public void G28_MovesToProfilePhysicalHome_NotMcsOriginOnly()
    {
        var def = MachineDefinition.CreateDefault();
        def.Axes.First(a => a.Name == "Z").Home = 15;
        def.McsZeroOffset = new MachineGeometryPoint { Z = 200 };
        def.SyncHomePositionFromAxes();

        var parser = new GCodeParser();
        def.ApplyHomeToMachineState(parser.State);
        parser.ProcessLine("G90 G0 X100 Y100 Z100", 1);
        parser.ProcessLine("G28", 2);

        var home = def.GetPhysicalHomePosition();
        Assert.Equal(home.X, parser.State.X, 3);
        Assert.Equal(home.Y, parser.State.Y, 3);
        Assert.Equal(home.Z, parser.State.Z, 3);
        Assert.NotEqual(def.McsZeroOffset.Z, parser.State.Z, 3);
    }

    [Fact]
    public void Validate_AllowsAxisHomeAtMinOrMax()
    {
        var def = MachineDefinition.CreateDefault();
        var z = def.Axes.First(a => a.Name == "Z");
        z.Min = 0;
        z.Max = 200;
        z.Home = 0;
        def.SyncHomePositionFromAxes();

        IReadOnlyList<string> errors = def.Validate();
        Assert.DoesNotContain(errors, e => e.Contains("HOME"));
    }

    [Fact]
    public void FinalizeProfileMcsAtSceneCenter_PreservesPhysicalHomeAndZerosMcsOffset()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { X = 100, Y = 50, Z = 200 };
        def.SyncHomePositionFromAxes();
        var homeBefore = def.GetPhysicalHomePosition();

        var physicalHome = def.GetPhysicalHomePosition();
        def.FinalizeProfileMcsAtSceneCenter(physicalHome.X, physicalHome.Y, physicalHome.Z);

        Assert.True(def.McsZeroOffset.IsNearlyZero());
        var homeAfter = def.GetPhysicalHomePosition();
        Assert.Equal(homeBefore.X, homeAfter.X, 2);
        Assert.Equal(homeBefore.Y, homeAfter.Y, 2);
        Assert.Equal(homeBefore.Z, homeAfter.Z, 2);
    }

    [Fact]
    public void EnsureMcsOriginAtSceneZero_PreservesPhysicalHome()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { X = 100, Y = 50, Z = 200 };
        def.SyncHomePositionFromAxes();
        var homeBefore = def.GetPhysicalHomePosition();

        def.EnsureMcsOriginAtSceneZero();

        Assert.True(def.McsZeroOffset.IsNearlyZero());
        var homeAfter = def.GetPhysicalHomePosition();
        Assert.Equal(homeBefore.X, homeAfter.X, 2);
        Assert.Equal(homeBefore.Y, homeAfter.Y, 2);
        Assert.Equal(homeBefore.Z, homeAfter.Z, 2);
    }

    [Fact]
    public void CommitMcsZeroAtPose_SetsHomeToMcsAndZerosAxisPose()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { X = 100, Y = 100, Z = 200 };
        var zAxis = def.Axes.First(a => a.Name == "Z");

        def.CommitMcsZeroAtPose(10, 20, 30);

        Assert.True(def.HomePosition.IsNearlyZero());
        Assert.Equal(0, zAxis.Home, 3);
        Assert.Equal(200, def.GetPhysicalHomePosition().Z, 3);
    }

    [Fact]
    public void NormalizeAfterLoad_ClearsAxisHomeDuplicatingMcsMarkerOffset()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = MachineMcsDefaults.DefaultBundledSpindleFaceMcsMarker.Clone();
        def.Axes.First(a => a.Name == "Y").Home = 110.882;
        def.Axes.First(a => a.Name == "X").Home = -0.366;

        def.NormalizeAfterLoad();

        Assert.Equal(0, def.Axes.First(a => a.Name == "Y").Home, 3);
        Assert.Equal(0, def.Axes.First(a => a.Name == "X").Home, 3);
        Assert.Equal(-110.882, def.GetPhysicalHomePosition().Y, 3);
        Assert.Equal(-0.366, def.GetPhysicalHomePosition().X, 3);
        Assert.Equal(880, def.GetPhysicalHomePosition().Z, 3);
    }

    [Fact]
    public void CommitMcsZeroAtPose_PreservesSpindleWorldOffsetAtHome()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { Z = 200 };
        var before = GetSpindleOffsetZ(KinematicChainSolver.SolveTransforms(def, 200, 200, 230));

        def.CommitMcsZeroAtPose(0, 0, 30);
        var after = GetSpindleOffsetZ(KinematicChainSolver.SolveTransforms(def, 200, 200, 200));

        Assert.Equal(before, after, 3);
    }

    private static double GetSpindleOffsetZ(IReadOnlyDictionary<string, Transform3D> pose) =>
        ((MatrixTransform3D)pose[MachineNodeIds.Spindle]).Value.OffsetZ;

    [Fact]
    public void KinematicChainSolver_TableMotionAxes_OnlySelectedAxesApply()
    {
        var def = MachineDefinition.CreateDefault();
        def.Table.MotionAxes = MachineProgramAxisMask.X;

        Matrix3D tableLowY = GetTableMatrix(KinematicChainSolver.SolveTransforms(def, 10, 0, 0));
        Matrix3D tableHighY = GetTableMatrix(KinematicChainSolver.SolveTransforms(def, 10, 200, 0));
        Assert.Equal(tableLowY, tableHighY);

        def.Table.MotionAxes = MachineProgramAxisMask.Xy;
        Matrix3D tableLowYFull = GetTableMatrix(KinematicChainSolver.SolveTransforms(def, 10, 0, 0));
        Matrix3D tableHighYFull = GetTableMatrix(KinematicChainSolver.SolveTransforms(def, 10, 200, 0));
        Assert.NotEqual(tableLowYFull, tableHighYFull);
    }

    private static Matrix3D GetTableMatrix(IReadOnlyDictionary<string, Transform3D> pose) =>
        ((MatrixTransform3D)pose[MachineNodeIds.Table]).Value;

    [Fact]
    public void KinematicChainSolver_WorkpieceMount_UsesTableCenterAndStoredZ()
    {
        var def = MachineDefinition.CreateDefault();
        def.WorkpieceMount = new MachineGeometryPoint { X = 10, Y = 20, Z = 5 };
        var transforms = KinematicChainSolver.SolveTransforms(def, def.HomePosition.X, def.HomePosition.Y, def.HomePosition.Z);
        Point3D mount = KinematicChainSolver.ComputeWorkpieceMountPoint(def, transforms);
        Assert.True(double.IsFinite(mount.X));
        Assert.True(double.IsFinite(mount.Y));
        Assert.True(double.IsFinite(mount.Z));
    }

    [Fact]
    public void StockWorkOrigin_CenterTop_UsesStockCenterAndMaxZ()
    {
        var bounds = new WorkpiecePlacement.StockBounds(0, 100, 10, 60, 5, 25);
        MachineGeometryPoint origin = StockWorkOrigin.Compute(bounds, StockWorkOriginXY.Center, StockWorkOriginZ.Top);
        Assert.Equal(50, origin.X, 3);
        Assert.Equal(35, origin.Y, 3);
        Assert.Equal(25, origin.Z, 3);
    }

    [Fact]
    public void StockWorkOrigin_CornerBottom_UsesMinCornerAndMinZ()
    {
        var bounds = new WorkpiecePlacement.StockBounds(0, 100, 10, 60, 5, 25);
        MachineGeometryPoint origin = StockWorkOrigin.Compute(
            bounds,
            StockWorkOriginXY.CornerMaxXMaxY,
            StockWorkOriginZ.Bottom);
        Assert.Equal(100, origin.X, 3);
        Assert.Equal(60, origin.Y, 3);
        Assert.Equal(5, origin.Z, 3);
    }

    [Fact]
    public void WorkpiecePlacement_AlignToMount_CentersStockOnMount()
    {
        var stock = new WorkpiecePlacement.StockBounds(-50, 50, -40, 40, 0, 30);
        WorkpiecePlacement.StockBounds aligned = WorkpiecePlacement.AlignToMount(stock, 100, 200, 10, 0);
        Assert.Equal(100, (aligned.MinX + aligned.MaxX) * 0.5, 3);
        Assert.Equal(200, (aligned.MinY + aligned.MaxY) * 0.5, 3);
        Assert.Equal(10, aligned.MinZ, 3);
        Assert.Equal(40, aligned.MaxZ, 3);
    }

    [Fact]
    public void Validate_ExtraNodeRequiresMotionAxesWhenLinked()
    {
        var def = MachineDefinition.CreateDefault();
        def.ExtraNodes.Add(new MachineExtraNodeDefinition
        {
            Id = "vise",
            DisplayName = "Тиски",
            ParentNodeId = MachineNodeIds.Table,
            MotionLink = MachineMotionLink.Table,
            MotionAxes = MachineProgramAxisMask.None
        });

        IReadOnlyList<string> errors = def.Validate();
        Assert.Contains(errors, e => e.Contains("Тиски"));
    }
}
