using CNCSS.Machine.Configuration;
using CNCSS.Machine.Model;

namespace CNCSS.Tests;

public sealed class MachineProfileImportTests
{
    [Fact]
    public void ImportProfileDirectory_CopiesJsonAndStl()
    {
        string root = Path.Combine(Path.GetTempPath(), "cncss-import-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source");
        string library = Path.Combine(root, "library");
        var libraryStore = new MachineProfileStore(library);

        try
        {
            var packStore = new MachineProfileStore(Path.Combine(root, "pack"));
            packStore.Save(MachineDefinition.CreateDefault("portable", "Переносимый станок"));
            source = packStore.GetProfileDirectory("portable");
            File.WriteAllText(Path.Combine(source, "table.stl"), "solid fake");

            string importedId = libraryStore.ImportProfileDirectory(source);
            Assert.Equal("portable", importedId);
            Assert.True(File.Exists(Path.Combine(libraryStore.GetProfileDirectory(importedId), "machine.json")));
            Assert.True(File.Exists(Path.Combine(libraryStore.GetProfileDirectory(importedId), "table.stl")));

            MachineDefinition loaded = libraryStore.Load(importedId);
            Assert.Equal("Переносимый станок", loaded.DisplayName);
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
    public void ImportProfileDirectory_AllocatesUniqueId_WhenExists()
    {
        string root = Path.Combine(Path.GetTempPath(), "cncss-import-dup-" + Guid.NewGuid().ToString("N"));
        var store = new MachineProfileStore(root);

        try
        {
            string source = Path.Combine(root, "external");
            Directory.CreateDirectory(source);
            store.Save(MachineDefinition.CreateDefault("dup", "Первый"));
            File.WriteAllText(Path.Combine(source, "machine.json"), """{"profileId":"dup","displayName":"Второй"}""");

            string importedId = store.ImportProfileDirectory(source);
            Assert.Equal("dup_2", importedId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
