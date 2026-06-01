using CNCSS.Vis;
using Xunit;

namespace CNCSS.Tests;

public sealed class CadMeshQualitySettingsTests
{
    [Theory]
    [InlineData(CadMeshQualityPreset.Draft, 1000, 6)]
    [InlineData(CadMeshQualityPreset.Standard, 1000, 2)]
    [InlineData(CadMeshQualityPreset.Fine, 1000, 0.8)]
    [InlineData(CadMeshQualityPreset.ExtraFine, 1000, 0.25)]
    public void ComputeDeflection_scales_with_preset(CadMeshQualityPreset preset, double diagonal, double expectedApprox)
    {
        CadMeshQualitySettings settings = CadMeshQualitySettings.ForPreset(preset);
        double deflection = settings.ComputeDeflectionMm(diagonal);
        Assert.InRange(deflection, expectedApprox * 0.9, expectedApprox * 1.1);
    }

    [Fact]
    public void Fine_is_more_detailed_than_draft()
    {
        double draft = CadMeshQualitySettings.ForPreset(CadMeshQualityPreset.Draft).ComputeDeflectionMm(500);
        double fine = CadMeshQualitySettings.ForPreset(CadMeshQualityPreset.Fine).ComputeDeflectionMm(500);
        Assert.True(fine < draft);
    }
}
