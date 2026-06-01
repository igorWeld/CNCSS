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
            new Point3D(0, 0, 0),
            new Point3D(10, 0, 0),
            radius: 2,
            fluteLength: 5);

        Assert.True(sweep.Field.Contains(new Point3D(5, 0, 1)));
        Assert.False(sweep.Field.Contains(new Point3D(5, 3, 1)));
        Assert.False(sweep.Field.Contains(new Point3D(5, 0, 8)));
    }

    [Fact]
    public void BooleanSubtract_ShouldReduceSolidCells()
    {
        var stock = BrepPrimitives.CreateBox(new Rect3D(-10, -10, -10, 20, 20, 20));
        var sweep = SweptVolumeBuilder.BuildCylinderSweep(
            new Point3D(-8, 0, -8),
            new Point3D(8, 0, -8),
            radius: 2,
            fluteLength: 10);

        BrepSolid result = BooleanSubtract.Subtract(stock, sweep, 1.0);
        Assert.NotNull(result.Mesh);
        Assert.True(result.Mesh.Positions.Count > 0);
        Assert.False(result.Field.Contains(new Point3D(0, 0, -5)));
        Assert.True(result.Field.Contains(new Point3D(0, 6, 0)));
    }
}
