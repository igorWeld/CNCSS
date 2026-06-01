using CNCSS.Logic;

namespace CNCSS.Tests;

public sealed class ProgramLoaderTests
{
    [Fact]
    public void Load_ReturnsLinesParserAndReferencedTools()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cncss-loader-{Guid.NewGuid():N}.nc");
        File.WriteAllLines(path, new[]
        {
            "T3 M6",
            "G90 G0 X1",
            "T1 M6"
        });

        try
        {
            var result = new ProgramLoader().Load(path);

            Assert.Equal(Path.GetFullPath(path), result.FullPath);
            Assert.Equal(3, result.Lines.Length);
            Assert.Equal(3, result.Parser.Commands.Count);
            Assert.Equal(new[] { 1, 3 }, result.ToolNumbers);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
