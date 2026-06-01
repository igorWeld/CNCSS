using CNCSS.Logic.NcPrograms;

namespace CNCSS.Tests;

public sealed class NcProgramCatalogServiceTests
{
    [Fact]
    public void CreateResolveAndDeleteProgram_WorksInCatalogDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cncss-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string currentPath = Path.Combine(dir, "O0001.nc");
        var service = new NcProgramCatalogService();

        try
        {
            string created = service.CreateProgram(currentPath, "O1234");

            Assert.True(File.Exists(created));
            Assert.True(service.TryResolveProgramFile(currentPath, "O1234", out string? resolved));
            Assert.Equal(created, resolved);

            service.DeleteProgram(created);
            Assert.False(File.Exists(created));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
