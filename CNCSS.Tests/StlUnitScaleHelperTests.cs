using CNCSS.Vis;

namespace CNCSS.Tests;

public sealed class StlUnitScaleHelperTests
{
    [Theory]
    [InlineData(0.635, 635, 1000)]
    [InlineData(635, 635, 1)]
    [InlineData(100, 100, 1)]
    public void MeterAndMmExtents_MapToSimulationMillimeters(double fileExtent, double expectedTarget, double expectedScale)
    {
        double target = StlUnitScaleHelper.InferTargetMaxExtentMm(fileExtent);
        double scale = StlUnitScaleHelper.ComputeMeshScale(fileExtent, target);

        Assert.Equal(expectedTarget, target, 3);
        Assert.Equal(expectedScale, scale, 3);
    }
}
