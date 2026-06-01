using CNCSS.Machine.Configuration;
using CNCSS.Machine.Model;

namespace CNCSS.Tests;

public sealed class MachineProfileStoreTests
{
    [Fact]
    public void SaveLoadRoundTrip_PreservesDisplayName()
    {
        string root = Path.Combine(Path.GetTempPath(), "cncss-machine-store-" + Guid.NewGuid().ToString("N"));
        var store = new MachineProfileStore(root);
        var definition = MachineDefinition.CreateDefault("test_profile", "Тестовый станок");

        try
        {
            store.Save(definition);
            var loaded = store.Load("test_profile");
            Assert.Equal("Тестовый станок", loaded.DisplayName);
            Assert.Equal("test_profile", loaded.ProfileId);
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
