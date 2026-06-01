using System.Windows;
using CNCSS.Machine.Configuration;
using CNCSS.Machine.Model;
using CNCSS.Vis;

namespace CNCSS.Tests;

public sealed class MachineProfileBootstrapTests
{
    [Fact]
    public void ApplyBundledDefaultMachine_ImportsBaseTableSpindleStl()
    {
        string stepPath = MachineProfileBootstrap.GetBundledStepPath();
        if (!File.Exists(stepPath))
        {
            return;
        }

        EnsureWpfApplication();
        OcctNativeLoader.EnsureInitialized();

        string root = Path.Combine(Path.GetTempPath(), "cncss-bootstrap-" + Guid.NewGuid().ToString("N"));
        var store = new MachineProfileStore(root);

        try
        {
            var definition = MachineDefinition.CreateDefault(
                MachineProfileBootstrap.DefaultProfileId,
                "Станок по умолчанию");
            MachineProfileAssemblyImporter.ImportStepAssembly(
                store,
                definition,
                stepPath,
                new StlMeshLoader());

            MachineDefinition loaded = store.Load(MachineProfileBootstrap.DefaultProfileId);

            Assert.Equal("base.stl", loaded.Base.StlFileName);
            Assert.Equal("table.stl", loaded.Table.StlFileName);
            Assert.Equal("spindle.stl", loaded.Spindle.StlFileName);
            Assert.True(loaded.Base.StlSourceMaxExtent > 1);
            Assert.True(loaded.Table.StlSourceMaxExtent > 1);
            Assert.True(loaded.Spindle.StlSourceMaxExtent > 1);
            Assert.True(File.Exists(Path.Combine(store.GetProfileDirectory("default"), "base.stl")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_RepairsOrphanStlFileNames()
    {
        string root = Path.Combine(Path.GetTempPath(), "cncss-repair-" + Guid.NewGuid().ToString("N"));
        var store = new MachineProfileStore(root);

        try
        {
            store.Save(MachineDefinition.CreateDefault("default", "Станок по умолчанию"));
            string dir = store.GetProfileDirectory("default");
            File.WriteAllText(Path.Combine(dir, "base.stl"), "solid orphan");

            MachineDefinition loaded = store.Load("default");
            Assert.Equal("base.stl", loaded.Base.StlFileName);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void EnsureWpfApplication()
    {
        if (Application.Current == null)
        {
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }
    }
}
