using System.Windows.Media.Media3D;
using CNCSS.Vis;
using Xunit;

namespace CNCSS.Tests;

public sealed class MachineSymmetricAlignerTests
{
    [Fact]
    public void DetectSymmetricAxis_faces_separated_along_X_returns_X()
    {
        var faceA = Pick(0, 5, 0, normalX: 1);
        var faceB = Pick(20, 5, 0, normalX: -1);

        Assert.Equal(SymmetricTranslationAxis.X, MachineSymmetricAligner.DetectSymmetricAxis(faceA, faceB));
    }

    [Fact]
    public void DetectSymmetricAxis_faces_separated_along_Y_returns_Y()
    {
        var faceA = Pick(3, 0, 0, normalY: 1);
        var faceB = Pick(3, 18, 0, normalY: -1);

        Assert.Equal(SymmetricTranslationAxis.Y, MachineSymmetricAligner.DetectSymmetricAxis(faceA, faceB));
    }

    [Fact]
    public void CenterMeshOnFaceMidpointXY_moves_only_X_when_faces_are_on_X()
    {
        var faceA = Pick(0, 0, 0, normalX: 1);
        var faceB = Pick(10, 0, 0, normalX: -1);
        var meshCenter = new Point3D(2, 7, 4);
        var kin = MatrixTransform3D.Identity;
        var mesh = new MatrixTransform3D(Matrix3D.Identity);

        MachineSurfaceAligner.MeshLayout layout = MachineSymmetricAligner.CenterMeshOnFaceMidpointXY(
            faceA, faceB, meshCenter, kin, mesh);

        Assert.InRange(layout.Offset.X, 2.9, 3.1);
        Assert.InRange(layout.Offset.Y, -0.1, 0.1);
        Assert.InRange(layout.Offset.Z, -0.1, 0.1);
    }

    [Fact]
    public void CenterMeshOnFaceMidpointXY_moves_only_Y_when_faces_are_on_Y()
    {
        var faceA = Pick(0, 0, 0, normalY: 1);
        var faceB = Pick(0, 10, 0, normalY: -1);
        var meshCenter = new Point3D(4, 2, 3);
        var kin = MatrixTransform3D.Identity;
        var mesh = new MatrixTransform3D(Matrix3D.Identity);

        MachineSurfaceAligner.MeshLayout layout = MachineSymmetricAligner.CenterMeshOnFaceMidpointXY(
            faceA, faceB, meshCenter, kin, mesh);

        Assert.InRange(layout.Offset.X, -0.1, 0.1);
        Assert.InRange(layout.Offset.Y, 2.9, 3.1);
        Assert.InRange(layout.Offset.Z, -0.1, 0.1);
    }

    private static MeshFacePick Pick(
        double x,
        double y,
        double z,
        double normalX = 0,
        double normalY = 0,
        double normalZ = 1) =>
        new()
        {
            NodeId = "n1",
            PointWorld = new Point3D(x, y, z),
            PointMeshLocal = new Point3D(x, y, z),
            NormalWorld = new Vector3D(normalX, normalY, normalZ),
            NormalMeshLocal = new Vector3D(normalX, normalY, normalZ)
        };
}
