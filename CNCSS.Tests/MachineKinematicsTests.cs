using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Machine.Model;

namespace CNCSS.Tests;

public sealed class MachineKinematicsTests
{
    [Fact]
    public void GetTargetPositionMcs_Table_IsXYZeroZ()
    {
        var def = MachineDefinition.CreateDefault();
        var target = MachineKinematics.GetTargetPositionMcs(def, def.Table, 100, 50, 200);

        Assert.Equal(100, target.X, 3);
        Assert.Equal(50, target.Y, 3);
        Assert.Equal(0, target.Z, 3);
    }

    [Fact]
    public void GetTargetPositionMcs_Spindle_IsZeroZeroZ()
    {
        var def = MachineDefinition.CreateDefault();
        var target = MachineKinematics.GetTargetPositionMcs(def, def.Spindle, 100, 50, 75);

        Assert.Equal(0, target.X, 3);
        Assert.Equal(0, target.Y, 3);
        Assert.Equal(75, target.Z, 3);
    }

    [Fact]
    public void ProgramAxisToPhysical_AddsWcsAndMcsOffset()
    {
        Assert.Equal(410, MachineKinematics.ProgramAxisToPhysical(100, 10, 300, isAbsolute: true), 3);
    }

    [Fact]
    public void SpindleAttachmentPoint_MovesWithPhysicalZ_NotStlCenter()
    {
        var def = MachineDefinition.CreateDefault();
        def.Spindle.AttachOnChild = new MachineGeometryPoint { X = 0, Y = 0, Z = -80 };

        double homeZ = def.GetPhysicalHomePosition().Z;
        Point3D low = MachineKinematics.GetMotionCouplingPointAssemblyMcs(def, MachineNodeIds.Spindle, 0, 0, homeZ - 40);
        Point3D high = MachineKinematics.GetMotionCouplingPointAssemblyMcs(def, MachineNodeIds.Spindle, 0, 0, homeZ + 40);

        Assert.Equal(80, high.Z - low.Z, 3);
    }

    [Fact]
    public void TrySetAttachmentPoint_PlacesCouplingAtTargetMcsOnZ()
    {
        var def = MachineDefinition.CreateDefault();
        double homeZ = def.GetPhysicalHomePosition().Z;
        double poseZ = homeZ + 25;
        var target = new Point3D(def.Spindle.AttachOnParent.X, def.Spindle.AttachOnParent.Y, 500);

        bool ok = MachineKinematics.TrySetAttachmentPointAssemblyMcs(
            def.Spindle,
            def,
            MachineNodeIds.Spindle,
            target,
            0,
            0,
            poseZ);

        Assert.True(ok);
        Point3D attach = MachineKinematics.GetAttachmentPointAssemblyMcs(
            def,
            MachineNodeIds.Spindle,
            0,
            0,
            poseZ);
        Assert.Equal(target.X, attach.X, 2);
        Assert.Equal(target.Y, attach.Y, 2);
        Assert.Equal(target.Z, attach.Z, 2);
    }

    [Fact]
    public void GetAttachmentPointScene_AddsMcsZeroOffset()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { X = 100, Y = 0, Z = 200 };
        double homeZ = def.GetPhysicalHomePosition().Z;

        Point3D assembly = MachineKinematics.GetAttachmentPointAssemblyMcs(
            def,
            MachineNodeIds.Spindle,
            0,
            0,
            homeZ);
        Point3D scene = MachineKinematics.GetAttachmentPointScene(def, MachineNodeIds.Spindle, 0, 0, homeZ);

        Assert.Equal(assembly.X + 100, scene.X, 3);
        Assert.Equal(assembly.Z + 200, scene.Z, 3);
    }

    [Fact]
    public void AlignSceneGeometrySoWorldPointAtOrigin_MovesAttachmentToSceneZero()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { X = 50, Y = 0, Z = 0 };
        double homeZ = def.GetPhysicalHomePosition().Z;

        Point3D before = MachineKinematics.GetAttachmentPointScene(
            def, MachineNodeIds.Spindle, 0, 0, homeZ);

        def.AlignSceneGeometrySoWorldPointAtOrigin(before.X, before.Y, before.Z);

        Assert.Equal(0, def.McsZeroOffset.X, 3);
        Assert.Equal(0, def.McsZeroOffset.Y, 3);
        Assert.Equal(0, def.McsZeroOffset.Z, 3);

        Point3D after = MachineKinematics.GetAttachmentPointScene(
            def, MachineNodeIds.Spindle, 0, 0, homeZ);
        Assert.Equal(0, after.X, 2);
        Assert.Equal(0, after.Y, 2);
        Assert.Equal(0, after.Z, 2);
    }

    [Fact]
    public void AlignSceneGeometrySoWorldPointAtOrigin_MovesMachineAsRigidAssembly()
    {
        var def = MachineDefinition.CreateDefault();
        def.Base.MeshOffset = new MachineGeometryPoint { X = 0, Y = 0, Z = 98 };
        def.Table.AttachOnParent = new MachineGeometryPoint { X = 250, Y = 250, Z = 50 };
        def.Spindle.AttachOnParent = new MachineGeometryPoint { X = 250, Y = 250, Z = 400 };
        double homeZ = def.GetPhysicalHomePosition().Z;

        Point3D baseBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Base, MachineGeometryPoint.Zero, 0, 0, homeZ);
        Point3D tableBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Table, MachineGeometryPoint.Zero, 0, 0, homeZ);
        Point3D spindleBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Spindle, MachineGeometryPoint.Zero, 0, 0, homeZ);

        Point3D target = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            MachineNodeIds.Spindle,
            new MachineGeometryPoint { X = 0, Y = 0, Z = 0 },
            0,
            0,
            homeZ);

        def.AlignSceneGeometrySoWorldPointAtOrigin(target.X, target.Y, target.Z);

        Point3D baseAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Base, MachineGeometryPoint.Zero, 0, 0, homeZ);
        Point3D tableAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Table, MachineGeometryPoint.Zero, 0, 0, homeZ);
        Point3D spindleAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Spindle, MachineGeometryPoint.Zero, 0, 0, homeZ);

        Assert.True(def.McsZeroOffset.IsNearlyZero());
        Assert.Equal(0, spindleAfter.X, 2);
        Assert.Equal(0, spindleAfter.Y, 2);
        Assert.Equal(0, spindleAfter.Z, 2);
        Assert.Equal((tableBefore - baseBefore).Length, (tableAfter - baseAfter).Length, 2);
        Assert.Equal((spindleBefore - tableBefore).Length, (spindleAfter - tableAfter).Length, 2);
        Assert.Equal(baseBefore.X - target.X, baseAfter.X, 2);
        Assert.Equal(baseBefore.Y - target.Y, baseAfter.Y, 2);
        Assert.Equal(baseBefore.Z - target.Z, baseAfter.Z, 2);
    }

    [Fact]
    public void TrySetAttachOnChildLocal_PreservesAttachmentAssemblyPosition()
    {
        var def = MachineDefinition.CreateDefault();
        def.Spindle.AttachOnChild = new MachineGeometryPoint { X = 10, Y = -5, Z = 20 };
        double homeZ = def.GetPhysicalHomePosition().Z;

        Point3D before = MachineKinematics.GetAttachmentPointAssemblyMcs(
            def, MachineNodeIds.Spindle, 0, 0, homeZ);

        var newLocal = new MachineGeometryPoint { X = 0, Y = 0, Z = 80 };
        bool ok = MachineKinematics.TrySetAttachOnChildLocalPreservingAttachmentAssembly(
            def.Spindle,
            def,
            MachineNodeIds.Spindle,
            newLocal,
            0,
            0,
            homeZ,
            out _);

        Assert.True(ok);
        Assert.Equal(newLocal.Z, def.Spindle.AttachOnChild.Z, 3);

        Point3D after = MachineKinematics.GetAttachmentPointAssemblyMcs(
            def, MachineNodeIds.Spindle, 0, 0, homeZ);
        Assert.Equal(before.X, after.X, 2);
        Assert.Equal(before.Y, after.Y, 2);
        Assert.Equal(before.Z, after.Z, 2);
    }

    [Fact]
    public void TrySetAttachmentPoint_PreservesMeshOriginInScene()
    {
        var def = MachineDefinition.CreateDefault();
        def.Table.MeshOffset = new MachineGeometryPoint { X = 15, Y = -10, Z = 25 };
        double homeZ = def.GetPhysicalHomePosition().Z;

        Point3D meshOriginBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            MachineNodeIds.Table,
            MachineGeometryPoint.Zero,
            0,
            0,
            homeZ);

        var target = new Point3D(200, 150, 80);
        bool ok = MachineKinematics.TrySetAttachmentPointAssemblyMcs(
            def.Table,
            def,
            MachineNodeIds.Table,
            target,
            0,
            0,
            homeZ);

        Assert.True(ok);

        Point3D meshOriginAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            MachineNodeIds.Table,
            MachineGeometryPoint.Zero,
            0,
            0,
            homeZ);

        Assert.Equal(meshOriginBefore.X, meshOriginAfter.X, 2);
        Assert.Equal(meshOriginBefore.Y, meshOriginAfter.Y, 2);
        Assert.Equal(meshOriginBefore.Z, meshOriginAfter.Z, 2);

        Point3D attach = MachineKinematics.GetAttachmentPointAssemblyMcs(
            def, MachineNodeIds.Table, 0, 0, homeZ);
        Assert.Equal(target.X, attach.X, 2);
        Assert.Equal(target.Y, attach.Y, 2);
        Assert.Equal(target.Z, attach.Z, 2);
    }

    [Fact]
    public void TrySetAttachOnChild_PreservesExtraChildMeshInScene()
    {
        var def = MachineDefinition.CreateDefault();
        var extra = MachineExtraNodeDefinition.CreateNew("Fixture", MachineNodeIds.Table);
        extra.MeshOffset = new MachineGeometryPoint { X = 5, Y = -10, Z = 15 };
        extra.AttachOnParent = new MachineGeometryPoint { X = 30, Y = 40, Z = 0 };
        def.ExtraNodes.Add(extra);

        double homeZ = def.GetPhysicalHomePosition().Z;
        Point3D extraOriginBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            extra.Id,
            MachineGeometryPoint.Zero,
            0,
            0,
            homeZ);

        bool ok = MachineKinematics.TrySetAttachOnChildLocalPreservingSubtreeLayout(
            def.Table,
            def,
            MachineNodeIds.Table,
            new MachineGeometryPoint { X = 80, Y = 50, Z = 10 },
            0,
            0,
            homeZ);

        Assert.True(ok);

        Point3D extraOriginAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            extra.Id,
            MachineGeometryPoint.Zero,
            0,
            0,
            homeZ);

        Assert.Equal(extraOriginBefore.X, extraOriginAfter.X, 2);
        Assert.Equal(extraOriginBefore.Y, extraOriginAfter.Y, 2);
        Assert.Equal(extraOriginBefore.Z, extraOriginAfter.Z, 2);
    }

    [Fact]
    public void TryApplyMeshAttachPreset_MovesAttachToMeshCenterInScene()
    {
        var def = MachineDefinition.CreateDefault();
        def.Table.AttachOnChild = MachineGeometryPoint.Zero;
        def.Table.MeshOffset = new MachineGeometryPoint { X = 10, Y = 20, Z = 30 };
        double homeZ = def.GetPhysicalHomePosition().Z;

        var bounds = new Rect3D(0, 0, 0, 100, 200, 80);
        MachineGeometryPoint meshCenter = StlMeshBoundsHelper.Center(bounds);
        Point3D expectedCenter = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            MachineNodeIds.Table,
            meshCenter,
            0,
            0,
            homeZ);

        bool ok = MachineKinematics.TryApplyMeshAttachPreset(
            def.Table,
            def,
            MachineNodeIds.Table,
            MeshAttachmentPreset.Center,
            bounds,
            0,
            0,
            homeZ,
            out Point3D attachScene);

        Assert.True(ok);
        Assert.Equal(expectedCenter.X, attachScene.X, 2);
        Assert.Equal(expectedCenter.Y, attachScene.Y, 2);
        Assert.Equal(expectedCenter.Z, attachScene.Z, 2);
    }
}
