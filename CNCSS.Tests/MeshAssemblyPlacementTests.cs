using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;
using CNCSS.Vis;
using Xunit;

namespace CNCSS.Tests;

public sealed class MeshAssemblyPlacementTests
{
    [Fact]
    public void ComputeMeshOffset_base_node_preserves_assembly_centroid()
    {
        var transforms = new Dictionary<string, Transform3D>(StringComparer.OrdinalIgnoreCase)
        {
            [MachineNodeIds.Base] = Transform3D.Identity
        };
        var centroid = new Point3D(120, 80, 40);
        const double scale = 0.1;

        MachineGeometryPoint offset = MeshAssemblyPlacement.ComputeMeshOffset(
            transforms,
            MachineNodeIds.Base,
            centroid,
            scale);

        Assert.InRange(offset.X, -1e-6, 1e-6);
        Assert.InRange(offset.Y, -1e-6, 1e-6);
        Assert.InRange(offset.Z, -1e-6, 1e-6);
    }

    [Fact]
    public void ComputeMeshOffset_table_node_compensates_kinematic_frame()
    {
        MachineDefinition def = MachineDefinition.CreateDefault("test", "test");
        IReadOnlyDictionary<string, Transform3D> transforms =
            KinematicChainSolver.SolveTransforms(def, def.HomePosition.X, def.HomePosition.Y, def.HomePosition.Z);

        var centroid = new Point3D(600, 400, 100);
        const double scale = 0.1;
        MachineGeometryPoint offset = MeshAssemblyPlacement.ComputeMeshOffset(
            transforms,
            MachineNodeIds.Table,
            centroid,
            scale);

        Matrix3D table = GetMatrix(transforms[MachineNodeIds.Table]);
        Point3D world = table.Transform(new Point3D(
            offset.X + centroid.X * scale,
            offset.Y + centroid.Y * scale,
            offset.Z + centroid.Z * scale));

        Assert.InRange(world.X, 59.9, 60.1);
        Assert.InRange(world.Y, 39.9, 40.1);
        Assert.InRange(world.Z, 9.9, 10.1);
    }

    [Fact]
    public void ComputeMeshOffset_uses_distinct_mesh_centroid_when_provided()
    {
        var transforms = new Dictionary<string, Transform3D>(StringComparer.OrdinalIgnoreCase)
        {
            [MachineNodeIds.Base] = Transform3D.Identity
        };

        MachineGeometryPoint offset = MeshAssemblyPlacement.ComputeMeshOffset(
            transforms,
            MachineNodeIds.Base,
            assemblyCentroid: new Point3D(100, 0, 0),
            meshCentroid: new Point3D(90, 0, 0),
            meshScale: 1);

        Assert.InRange(offset.X, 9.9, 10.1);
        Assert.InRange(offset.Y, -1e-6, 1e-6);
        Assert.InRange(offset.Z, -1e-6, 1e-6);
    }

    private static Matrix3D GetMatrix(Transform3D transform) =>
        transform is MatrixTransform3D matrix ? matrix.Matrix : Matrix3D.Identity;
}
