using CNCSS.Vis;

namespace CNCSS.Tests;

public sealed class MeshImportFormatsTests
{
    [Theory]
    [InlineData("part.stl", true)]
    [InlineData("part.obj", true)]
    [InlineData("part.step", true)]
    [InlineData("part.STP", true)]
    [InlineData("part.iges", true)]
    [InlineData("part.IGS", true)]
    [InlineData("part.nc", false)]
    public void IsSupported_recognizes_mesh_extensions(string file, bool expected) =>
        Assert.Equal(expected, MeshImportFormats.IsSupported(file));
}
