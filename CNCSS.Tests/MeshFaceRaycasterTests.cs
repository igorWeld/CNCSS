using System.Windows.Media.Media3D;
using CNCSS.Vis;
using HelixToolkit.Wpf;
using Xunit;

namespace CNCSS.Tests;

public sealed class MeshFaceRaycasterTests
{
    [Fact]
    public void ExpandCoplanarPatch_merges_flat_end_cap_triangles()
    {
        var mesh = BuildFlatDiscMesh(radius: 10, segments: 16);
        MeshFaceRaycaster.ExpandCoplanarPatch(mesh, 0, out Point3D center, out Vector3D normal);

        Assert.InRange(center.Z, -1e-6, 1e-6);
        Assert.InRange(normal.Z, 0.99, 1.01);
    }

    [Fact]
    public void CollectCoplanarPatchTriangleIndices_includes_all_disc_triangles()
    {
        var mesh = BuildFlatDiscMesh(radius: 10, segments: 16);

        IReadOnlyList<int> triangles = MeshFaceRaycaster.CollectCoplanarPatchTriangleIndices(mesh, 0);

        Assert.Equal(16, triangles.Count);
    }

    [Fact]
    public void BuildCoplanarPatchWorld_contains_all_disc_vertices()
    {
        var mesh = BuildFlatDiscMesh(radius: 10, segments: 12);

        MeshGeometry3D patch = MeshFaceRaycaster.BuildCoplanarPatchWorld(mesh, 0, Transform3D.Identity);

        Assert.Equal(36, patch.Positions.Count);
        Assert.Equal(36, patch.TriangleIndices.Count);
    }

    [Fact]
    public void TryPick_hits_back_facing_end_cap()
    {
        var mesh = BuildFlatDiscMesh(radius: 10, segments: 12, flipWinding: true);
        var geometry = new GeometryModel3D { Geometry = mesh };
        var slot = new NodeSlotInfo
        {
            NodeId = "spindle",
            KinematicTransform = Transform3D.Identity,
            MeshTransform = Transform3D.Identity,
            MeshToWorld = Transform3D.Identity
        };
        var targets = new[]
        {
            new NodeMeshPickTarget
            {
                NodeId = "spindle",
                Slot = slot,
                Geometry = geometry
            }
        };

        var ray = new Ray3D(new Point3D(0, 0, 5), new Vector3D(0, 0, -1));
        bool ok = MeshFaceRaycaster.TryPick(ray, targets, out MeshFacePick pick, out _, out _);

        Assert.True(ok);
        Assert.InRange(pick.NormalWorld.Z, 0.9, 1.1);
    }

    [Fact]
    public void TryPick_prefers_end_cap_over_side_wall_on_oblique_view()
    {
        MeshGeometry3D mesh = BuildCylinderMesh(radius: 10, height: 30, segments: 24);
        var geometry = new GeometryModel3D { Geometry = mesh };
        var slot = new NodeSlotInfo
        {
            NodeId = "spindle",
            KinematicTransform = Transform3D.Identity,
            MeshTransform = Transform3D.Identity,
            MeshToWorld = Transform3D.Identity
        };
        var targets = new[]
        {
            new NodeMeshPickTarget
            {
                NodeId = "spindle",
                Slot = slot,
                Geometry = geometry
            }
        };

        var ray = new Ray3D(new Point3D(2, 0, 40), new Vector3D(0, 0, -1));
        bool ok = MeshFaceRaycaster.TryPick(ray, targets, out MeshFacePick pick, out _, out _);

        Assert.True(ok);
        Assert.InRange(pick.NormalWorld.Z, 0.9, 1.01);
    }

    private static MeshGeometry3D BuildCylinderMesh(double radius, double height, int segments)
    {
        var builder = new MeshBuilder(false, false);
        builder.AddCylinder(new Point3D(0, 0, 0), new Point3D(0, 0, height), radius, segments, false, true);
        return builder.ToMesh();
    }

    private static MeshGeometry3D BuildFlatDiscMesh(double radius, int segments, bool flipWinding = false)
    {
        var builder = new MeshBuilder(false, false);
        var center = new Point3D(0, 0, 0);
        for (int i = 0; i < segments; i++)
        {
            double a0 = i * 2 * Math.PI / segments;
            double a1 = (i + 1) * 2 * Math.PI / segments;
            var p0 = new Point3D(Math.Cos(a0) * radius, Math.Sin(a0) * radius, 0);
            var p1 = new Point3D(Math.Cos(a1) * radius, Math.Sin(a1) * radius, 0);
            if (flipWinding)
            {
                builder.AddTriangle(center, p1, p0);
            }
            else
            {
                builder.AddTriangle(center, p0, p1);
            }
        }

        return builder.ToMesh();
    }
}
