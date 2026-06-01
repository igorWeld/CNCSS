using CNCSS.Logic;
using CNCSS.Simulation.Execution;

namespace CNCSS.Tests;

public sealed class ProgramWorkspaceTests
{
    [Fact]
    public void Load_StoresCurrentProgramAndClearResetsIt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cncss-workspace-{Guid.NewGuid():N}.nc");
        File.WriteAllLines(path, new[] { "T2 M6", "G90 G0 X10" });
        var workspace = new ProgramWorkspace(new ProgramLoader(), new ProgramStateService());

        try
        {
            workspace.Load(path);

            Assert.True(workspace.HasProgram);
            Assert.Equal(2, workspace.Lines.Length);
            Assert.Equal(new[] { 2 }, workspace.ToolNumbers);
            Assert.Equal(10, workspace.GetPositionAtUiLine(2).X, precision: 6);

            workspace.Clear();

            Assert.False(workspace.HasProgram);
            Assert.Empty(workspace.Lines);
            Assert.Null(workspace.Parser);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
