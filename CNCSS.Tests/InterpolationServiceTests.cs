using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Simulation.Execution;

namespace CNCSS.Tests;

public sealed class InterpolationServiceTests
{
    [Fact]
    public void InitializeSegment_ForZeroLengthMove_CompletesImmediately()
    {
        var point = new Point3D(1, 2, 3);

        var result = InterpolationService.InitializeSegment(point, point);

        Assert.Equal(1.0, result.progress, precision: 6);
        Assert.Equal(100, result.intervalMs);
    }

    [Fact]
    public void AdvanceProgress_ForZeroDistance_ReturnsComplete()
    {
        var point = new Point3D(0, 0, 0);

        double progress = InterpolationService.AdvanceProgress(
            0.25,
            point,
            point,
            speedMmPerSec: 50,
            simulationMultiplier: 1,
            fpsSlowdownFactor: 1);

        Assert.Equal(1.0, progress, precision: 6);
    }

    [Fact]
    public void ComputePosition_WithoutArc_UsesLinearInterpolation()
    {
        var start = new Point3D(0, 0, 0);
        var target = new Point3D(10, 20, 30);

        Point3D point = InterpolationService.ComputePosition(start, target, 0.5, arc: null);

        Assert.Equal(5, point.X, precision: 6);
        Assert.Equal(10, point.Y, precision: 6);
        Assert.Equal(15, point.Z, precision: 6);
    }

    [Theory]
    [InlineData(17, 5.0, -5.0, 0.0)]
    [InlineData(18, 5.0, 7.0, -5.0)]
    [InlineData(19, 0.0, 5.0, -5.0)]
    public void ComputePosition_WithArc_UsesPlaneCoordinates(int plane, double expectedX, double expectedY, double expectedZ)
    {
        var arc = new ArcGeometry
        {
            Plane = plane,
            Radius = 5,
            CenterU = 5,
            CenterV = 0,
            StartAngleRad = Math.PI,
            SweepAngleRad = Math.PI,
            StartX = plane == 19 ? 0 : 0,
            StartY = plane == 17 ? 0 : plane == 19 ? 0 : 7,
            StartZ = plane == 18 ? 0 : plane == 17 ? 0 : 0,
            EndX = plane == 19 ? 0 : 10,
            EndY = plane == 17 ? 0 : plane == 19 ? 10 : 7,
            EndZ = plane == 18 ? 10 : plane == 17 ? 0 : 10
        };

        Point3D point = InterpolationService.ComputePosition(
            new Point3D(0, 0, 0),
            new Point3D(10, 10, 10),
            0.5,
            arc);

        Assert.Equal(expectedX, point.X, precision: 6);
        Assert.Equal(expectedY, point.Y, precision: 6);
        Assert.Equal(expectedZ, point.Z, precision: 6);
    }
}
