using CNCSS.Geometry.BRep;
using CNCSS.Geometry.Boolean;
using CNCSS.Geometry.SweptVolume;
using System.Windows.Media.Media3D;

namespace CNCSS.Tests;

public class SweptAndBooleanTests
{
    [Fact]
    public void CylinderSweepField_ShouldContainPointsInToolPath()
    {
        var sweep = SweptVolumeBuilder.BuildCylinderSweep(
            new Point3D(0, 0, 10),
            new Point3D(10, 0, 10),
            radius: 2,
            fluteLength: 5);

        Assert.True(sweep.Field.Contains(new Point3D(5, 0, 10)));
        Assert.True(sweep.Field.Contains(new Point3D(5, 0, 9.95)));
        Assert.True(sweep.Field.Contains(new Point3D(5, 0, 12)));
        Assert.False(sweep.Field.Contains(new Point3D(5, 0, 9)));
        Assert.False(sweep.Field.Contains(new Point3D(5, 3, 10)));
        Assert.False(sweep.Field.Contains(new Point3D(5, 0, 16)));
    }

    [Fact]
    public void CylinderSweepField_RampUsesXyProjectionForVerticalTool()
    {
        var sweep = SweptVolumeBuilder.BuildCylinderSweep(
            new Point3D(0, 0, 0),
            new Point3D(10, 0, 10),
            radius: 1,
            fluteLength: 2);

        Assert.True(sweep.Field.Contains(new Point3D(5, 0, 6.5)));
        Assert.False(sweep.Field.Contains(new Point3D(5, 2, 5)));
    }

    [Fact]
    public void CylinderSweepField_VerticalMoveSweepsCuttingVolume()
    {
        var sweep = SweptVolumeBuilder.BuildCylinderSweep(
            new Point3D(0, 0, 10),
            new Point3D(0, 0, 0),
            radius: 1,
            fluteLength: 2);

        Assert.True(sweep.Field.Contains(new Point3D(0, 0, 11.5)));
        Assert.True(sweep.Field.Contains(new Point3D(0, 0, 0.5)));
        Assert.False(sweep.Field.Contains(new Point3D(0, 0, 12.5)));
        Assert.False(sweep.Field.Contains(new Point3D(0, 0, -0.5)));
    }

    [Fact]
    public void BooleanSubtract_ShouldReduceSolidCells()
    {
        var stock = BrepPrimitives.CreateBox(new Rect3D(-10, -10, -10, 20, 20, 20));
        var sweep = SweptVolumeBuilder.BuildCylinderSweep(
            new Point3D(-8, 0, -5),
            new Point3D(8, 0, -5),
            radius: 2,
            fluteLength: 10);

        BrepSolid result = BooleanSubtract.Subtract(stock, sweep, 1.0);
        Assert.NotNull(result.Mesh);
        Assert.True(result.Mesh.Positions.Count > 0);
        Assert.False(result.Field.Contains(new Point3D(0, 0, 2)));
        Assert.True(result.Field.Contains(new Point3D(0, 6, 0)));
    }
}
