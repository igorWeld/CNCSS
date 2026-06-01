using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;
using Xunit;

namespace CNCSS.Tests;

public sealed class MachineMcsOriginPreserveTests
{
    [Fact]
    public void MoveMcsOrigin_shifts_attach_in_mcs_by_negative_delta()
    {
        var def = MachineDefinition.CreateDefault();
        double attachX = def.Table.AttachOnParent.X;

        def.MoveMcsOriginPreservingSceneGeometry(new MachineGeometryPoint { X = 100, Y = 0, Z = 0 });

        Assert.Equal(attachX - 100, def.Table.AttachOnParent.X, 3);
    }

    [Fact]
    public void MoveMcsOrigin_preserves_table_origin_in_scene_with_assembly_shift()
    {
        var def = MachineDefinition.CreateDefault();
        def.Table.AttachOnParent = new MachineGeometryPoint { X = 200, Y = 100, Z = 50 };
        def.McsZeroOffset = MachineGeometryPoint.Zero;

        Point3D before = GetSceneTableOrigin(def);
        def.MoveMcsOriginPreservingSceneGeometry(new MachineGeometryPoint { X = 80, Y = -20, Z = 50 });
        Point3D after = GetSceneTableOrigin(def);

        Assert.Equal(before.X, after.X, 2);
        Assert.Equal(before.Y, after.Y, 2);
        Assert.Equal(before.Z, after.Z, 2);
    }

    [Fact]
    public void MoveMcsOrigin_shifts_g54_and_preserves_extra_on_table_in_scene()
    {
        var def = MachineDefinition.CreateDefault();
        var extra = MachineExtraNodeDefinition.CreateNew("Fixture", MachineNodeIds.Table);
        extra.MeshOffset = new MachineGeometryPoint { X = 5, Y = 0, Z = 0 };
        def.ExtraNodes.Add(extra);
        def.DefaultWorkOffsetG54 = new MachineGeometryPoint { X = 10, Y = 20, Z = 30 };

        double homeZ = def.GetPhysicalHomePosition().Z;
        Point3D extraBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            extra.Id,
            MachineGeometryPoint.Zero,
            0,
            0,
            homeZ);

        def.MoveMcsOriginPreservingSceneGeometry(
            new MachineGeometryPoint { X = 50, Y = -30, Z = 10 },
            0,
            0,
            homeZ);

        Assert.Equal(-40, def.DefaultWorkOffsetG54.X, 3);
        Assert.Equal(50, def.DefaultWorkOffsetG54.Y, 3);
        Assert.Equal(20, def.DefaultWorkOffsetG54.Z, 3);

        Point3D extraAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            extra.Id,
            MachineGeometryPoint.Zero,
            0,
            0,
            homeZ);
        Assert.Equal(extraBefore.X, extraAfter.X, 2);
        Assert.Equal(extraBefore.Y, extraAfter.Y, 2);
        Assert.Equal(extraBefore.Z, extraAfter.Z, 2);
    }

    [Fact]
    public void MoveMcsOrigin_shifts_node_positions_in_mcs_by_mcs_delta()
    {
        var def = MachineDefinition.CreateDefault();
        def.Base.MeshOffset = new MachineGeometryPoint { X = 0, Y = 0, Z = 98 };
        double homeZ = def.GetPhysicalHomePosition().Z;

        Point3D baseMcsBefore = MachineKinematics.GetNodeMeshOriginAssemblyMcs(
            def, MachineNodeIds.Base, 0, 0, homeZ);
        Point3D spindleMcsBefore = MachineKinematics.GetNodeMeshOriginAssemblyMcs(
            def, MachineNodeIds.Spindle, 0, 0, homeZ);
        Assert.Equal(98, baseMcsBefore.Z, 1);

        Point3D baseScene = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Base, MachineGeometryPoint.Zero, 0, 0, homeZ);

        def.MoveMcsOriginPreservingSceneGeometry(
            new MachineGeometryPoint { X = 50, Y = 30, Z = 10 },
            0,
            0,
            homeZ);

        Point3D baseMcsAfter = MachineKinematics.GetNodeMeshOriginAssemblyMcs(
            def, MachineNodeIds.Base, 0, 0, homeZ);
        Assert.Equal(baseMcsBefore.X - 50, baseMcsAfter.X, 1);
        Assert.Equal(baseMcsBefore.Y - 30, baseMcsAfter.Y, 1);
        Assert.Equal(baseMcsBefore.Z - 10, baseMcsAfter.Z, 1);

        Point3D baseSceneAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Base, MachineGeometryPoint.Zero, 0, 0, homeZ);
        Assert.Equal(baseScene.X, baseSceneAfter.X, 2);
        Assert.Equal(baseScene.Y, baseSceneAfter.Y, 2);
        Assert.Equal(baseScene.Z, baseSceneAfter.Z, 2);

        Point3D spindleMcsAfter = MachineKinematics.GetNodeMeshOriginAssemblyMcs(
            def, MachineNodeIds.Spindle, 0, 0, homeZ);
        Assert.Equal(spindleMcsBefore.X - 50, spindleMcsAfter.X, 1);
        Assert.Equal(spindleMcsBefore.Y - 30, spindleMcsAfter.Y, 1);
        Assert.Equal(spindleMcsBefore.Z - 10, spindleMcsAfter.Z, 1);
    }

    [Fact]
    public void MoveMcsOrigin_preserves_table_mesh_in_scene_with_mesh_offset()
    {
        var def = MachineDefinition.CreateDefault();
        def.Base.MeshOffset = new MachineGeometryPoint { X = 0, Y = 0, Z = 98 };
        def.Table.AttachOnParent = new MachineGeometryPoint { X = 250, Y = 250, Z = 50 };
        def.Table.MeshOffset = new MachineGeometryPoint { X = 12, Y = -8, Z = 25 };
        def.Spindle.AttachOnParent = new MachineGeometryPoint { X = 250, Y = 250, Z = 400 };
        double homeZ = def.GetPhysicalHomePosition().Z;

        Point3D tableBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Table, MachineGeometryPoint.Zero, 0, 0, homeZ);
        Point3D baseBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Base, MachineGeometryPoint.Zero, 0, 0, homeZ);

        def.PlaceMcsOriginAtSceneWithWorldAtZero(
            new MachineGeometryPoint { X = 120, Y = 60, Z = 350 },
            0,
            0,
            homeZ);

        Point3D tableAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Table, MachineGeometryPoint.Zero, 0, 0, homeZ);
        Point3D baseAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Base, MachineGeometryPoint.Zero, 0, 0, homeZ);

        Assert.Equal(tableBefore.X, tableAfter.X, 1);
        Assert.Equal(tableBefore.Y, tableAfter.Y, 1);
        Assert.Equal(tableBefore.Z, tableAfter.Z, 1);
        Assert.Equal(baseBefore.X, baseAfter.X, 1);
        Assert.Equal(baseBefore.Y, baseAfter.Y, 1);
        Assert.Equal(baseBefore.Z, baseAfter.Z, 1);
    }

    [Fact]
    public void PlaceMcsOriginAtSceneWithWorldAtZero_rebases_attachments_not_noop()
    {
        var def = MachineDefinition.CreateDefault();
        def.Base.MeshOffset = new MachineGeometryPoint { X = 0, Y = 0, Z = 98 };
        double homeZ = def.GetPhysicalHomePosition().Z;
        double baseZBefore = def.Base.MeshOffset.Z;

        def.PlaceMcsOriginAtSceneWithWorldAtZero(
            new MachineGeometryPoint { X = 40, Y = 20, Z = 500 },
            0,
            0,
            homeZ);

        Assert.Equal(40, def.McsZeroOffset.X, 1);
        Assert.NotEqual(baseZBefore, def.Base.MeshOffset.Z);
    }

    [Fact]
    public void MoveMcsOrigin_to_spindle_mesh_center_preserves_spindle_in_scene()
    {
        var def = MachineDefinition.CreateDefault();
        def.Spindle.MeshOffset = new MachineGeometryPoint { X = -250, Y = -250, Z = -400 };
        double homeZ = def.GetPhysicalHomePosition().Z;

        var bounds = new Rect3D(0, 0, 0, 80, 80, 200);
        Point3D spindleCenterScene = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            MachineNodeIds.Spindle,
            StlMeshBoundsHelper.Center(bounds),
            0,
            0,
            homeZ);

        Point3D meshOriginBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            MachineNodeIds.Spindle,
            MachineGeometryPoint.Zero,
            0,
            0,
            homeZ);

        def.PlaceMcsOriginAtSceneWithWorldAtZero(
            new MachineGeometryPoint
            {
                X = spindleCenterScene.X,
                Y = spindleCenterScene.Y,
                Z = spindleCenterScene.Z
            },
            0,
            0,
            homeZ);

        Point3D meshOriginAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            MachineNodeIds.Spindle,
            MachineGeometryPoint.Zero,
            0,
            0,
            homeZ);

        Assert.Equal(spindleCenterScene.X, def.McsZeroOffset.X, 2);
        Assert.Equal(spindleCenterScene.Y, def.McsZeroOffset.Y, 2);
        Assert.Equal(spindleCenterScene.Z, def.McsZeroOffset.Z, 2);
        Point3D mcsPanelWorld = MachineAttachmentService.GetMcsOriginScene(def);
        Assert.Equal(0, mcsPanelWorld.X, 2);
        Assert.Equal(0, mcsPanelWorld.Y, 2);
        Assert.Equal(0, mcsPanelWorld.Z, 2);
        Assert.Equal(meshOriginBefore.X, meshOriginAfter.X, 2);
        Assert.Equal(meshOriginBefore.Y, meshOriginAfter.Y, 2);
        Assert.Equal(meshOriginBefore.Z, meshOriginAfter.Z, 2);
    }

    [Fact]
    public void MoveMcsOrigin_to_zero_after_spindle_place_preserves_table_mesh()
    {
        var def = MachineDefinition.CreateDefault();
        def.Table.MeshOffset = new MachineGeometryPoint { X = 10, Y = -5, Z = 20 };
        double homeZ = def.GetPhysicalHomePosition().Z;

        Point3D spindleCenter = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            MachineNodeIds.Spindle,
            StlMeshBoundsHelper.Center(new Rect3D(0, 0, 0, 80, 80, 200)),
            0,
            0,
            homeZ);

        def.PlaceMcsOriginAtSceneWithWorldAtZero(
            new MachineGeometryPoint { X = spindleCenter.X, Y = spindleCenter.Y, Z = spindleCenter.Z },
            0,
            0,
            homeZ);

        Point3D tableBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Table, MachineGeometryPoint.Zero, 0, 0, homeZ);

        def.MoveMcsOriginPreservingSceneGeometry(MachineGeometryPoint.Zero, 0, 0, homeZ);

        Point3D tableAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Table, MachineGeometryPoint.Zero, 0, 0, homeZ);

        Assert.True(def.McsZeroOffset.IsNearlyZero());
        Assert.Equal(tableBefore.X, tableAfter.X, 1);
        Assert.Equal(tableBefore.Y, tableAfter.Y, 1);
        Assert.Equal(tableBefore.Z, tableAfter.Z, 1);
    }

    [Fact]
    public void FinalizeProfileMcsAtSceneCenter_preserves_table_mesh_after_mcs_at_spindle()
    {
        var def = MachineDefinition.CreateDefault();
        def.Base.MeshOffset = new MachineGeometryPoint { X = 0, Y = 0, Z = 98 };
        def.Table.MeshOffset = new MachineGeometryPoint { X = 10, Y = -5, Z = 20 };
        double homeZ = def.GetPhysicalHomePosition().Z;

        Point3D spindleCenter = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            MachineNodeIds.Spindle,
            StlMeshBoundsHelper.Center(new Rect3D(0, 0, 0, 80, 80, 200)),
            0,
            0,
            homeZ);

        def.PlaceMcsOriginAtSceneWithWorldAtZero(
            new MachineGeometryPoint { X = spindleCenter.X, Y = spindleCenter.Y, Z = spindleCenter.Z },
            0,
            0,
            homeZ);

        Point3D tableBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Table, MachineGeometryPoint.Zero, 0, 0, homeZ);

        MachineGeometryPoint physicalHome = def.GetPhysicalHomePosition();
        def.FinalizeProfileMcsAtSceneCenter(physicalHome.X, physicalHome.Y, physicalHome.Z);

        Point3D tableAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Table, MachineGeometryPoint.Zero, 0, 0, homeZ);

        Assert.True(def.McsZeroOffset.IsNearlyZero());
        Assert.Equal(tableBefore.X, tableAfter.X, 1);
        Assert.Equal(tableBefore.Y, tableAfter.Y, 1);
        Assert.Equal(tableBefore.Z, tableAfter.Z, 1);
    }

    [Fact]
    public void PlaceMcs_at_base_bottom_preserves_table_and_spindle_mesh_in_scene()
    {
        var def = MachineDefinition.CreateDefault();
        def.Base.MeshOffset = new MachineGeometryPoint { X = 0, Y = 0, Z = 98 };
        double homeZ = def.GetPhysicalHomePosition().Z;
        var bounds = new Rect3D(0, 0, 0, 500, 500, 200);
        MachineGeometryPoint bottomLocal = StlMeshBoundsHelper.BottomCenter(bounds);
        Point3D mcsTarget = MachineAttachmentSceneMath.MeshLocalToScene(
            def,
            MachineNodeIds.Base,
            bottomLocal,
            0,
            0,
            homeZ);

        Point3D tableBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Table, MachineGeometryPoint.Zero, 0, 0, homeZ);
        Point3D spindleBefore = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Spindle, MachineGeometryPoint.Zero, 0, 0, homeZ);

        def.PlaceMcsOriginAtSceneWithWorldAtZero(
            new MachineGeometryPoint { X = mcsTarget.X, Y = mcsTarget.Y, Z = mcsTarget.Z },
            0,
            0,
            homeZ);

        Point3D tableAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Table, MachineGeometryPoint.Zero, 0, 0, homeZ);
        Point3D spindleAfter = MachineAttachmentSceneMath.MeshLocalToScene(
            def, MachineNodeIds.Spindle, MachineGeometryPoint.Zero, 0, 0, homeZ);

        Assert.Equal(mcsTarget.X, def.McsZeroOffset.X, 1);
        Assert.Equal(mcsTarget.Y, def.McsZeroOffset.Y, 1);
        Assert.Equal(mcsTarget.Z, def.McsZeroOffset.Z, 1);
        Assert.Equal(tableBefore.X, tableAfter.X, 1);
        Assert.Equal(tableBefore.Y, tableAfter.Y, 1);
        Assert.Equal(tableBefore.Z, tableAfter.Z, 1);
        Assert.Equal(spindleBefore.X, spindleAfter.X, 1);
        Assert.Equal(spindleBefore.Y, spindleAfter.Y, 1);
        Assert.Equal(spindleBefore.Z, spindleAfter.Z, 1);
    }

    private static Point3D GetSceneTableOrigin(MachineDefinition def)
    {
        var mcs = def.McsZeroOffset ?? MachineGeometryPoint.Zero;
        Transform3D table = KinematicChainSolver.SolveTransforms(def, 0, 0, 0)[MachineNodeIds.Table];
        Point3D local = table.Transform(new Point3D(0, 0, 0));
        return new Point3D(local.X + mcs.X, local.Y + mcs.Y, local.Z + mcs.Z);
    }
}
