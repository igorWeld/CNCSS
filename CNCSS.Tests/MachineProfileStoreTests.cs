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

    [Fact]
    public void SaveLoadRoundTrip_PreservesToolMountAndWorkpieceMount()
    {
        string root = Path.Combine(Path.GetTempPath(), "cncss-machine-store-" + Guid.NewGuid().ToString("N"));
        var store = new MachineProfileStore(root);
        var definition = MachineDefinition.CreateDefault("mount_test", "Mount test");
        definition.ToolMountNodeId = MachineNodeIds.Spindle;
        definition.ToolMount = new MachineGeometryPoint { X = 12.5, Y = -3, Z = 480 };
        definition.ToolMountIsNodeLocal = true;
        definition.WorkpieceMount = new MachineGeometryPoint { X = 10, Y = 20, Z = 155 };
        definition.FixtureHeightMm = 12;

        try
        {
            store.Save(definition);
            var loaded = store.Load("mount_test");

            Assert.True(loaded.ToolMountIsNodeLocal);
            Assert.Equal(12.5, loaded.ToolMount.X, 3);
            Assert.Equal(-3, loaded.ToolMount.Y, 3);
            Assert.Equal(480, loaded.ToolMount.Z, 3);
            Assert.Equal(10, loaded.WorkpieceMount.X, 3);
            Assert.Equal(20, loaded.WorkpieceMount.Y, 3);
            Assert.Equal(155, loaded.WorkpieceMount.Z, 3);
            Assert.Equal(12, loaded.FixtureHeightMm, 3);
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
    public void SaveFactorySnapshot_RestoreAsDefault_PreservesCustomSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), "cncss-machine-store-" + Guid.NewGuid().ToString("N"));
        string factoryRoot = Path.Combine(Path.GetTempPath(), "cncss-factory-" + Guid.NewGuid().ToString("N"));
        var store = new MachineProfileStore(root, factoryRoot);
        var definition = MachineDefinition.CreateDefault("default", "Заводской тест");
        definition.DefaultToolStickOutMm = 77;
        definition.FixtureHeightMm = 15;

        try
        {
            store.Save(definition);
            store.SaveFactorySnapshotFromProfile("default");
            definition.DefaultToolStickOutMm = 10;
            store.Save(definition);

            var restored = store.RestoreFactorySnapshotAsDefault();
            Assert.Equal(77, restored.DefaultToolStickOutMm, 3);
            Assert.Equal(15, restored.FixtureHeightMm, 3);
            Assert.Equal("Заводской тест", restored.DisplayName);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(factoryRoot))
            {
                Directory.Delete(factoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void FinalizeProfileMcsAtSceneCenter_DoesNotShiftNodeLocalToolMountOrWorkpieceMount()
    {
        var def = MachineDefinition.CreateDefault();
        def.McsZeroOffset = new MachineGeometryPoint { X = 100, Y = 50, Z = 200 };
        def.ToolMount = new MachineGeometryPoint { X = 1, Y = 2, Z = 3 };
        def.ToolMountIsNodeLocal = true;
        def.WorkpieceMount = new MachineGeometryPoint { X = 10, Y = 20, Z = 30 };
        def.FixtureHeightMm = 5;

        def.FinalizeProfileMcsAtSceneCenter(0, 0, 0);

        Assert.Equal(1, def.ToolMount.X, 3);
        Assert.Equal(2, def.ToolMount.Y, 3);
        Assert.Equal(3, def.ToolMount.Z, 3);
        Assert.Equal(10, def.WorkpieceMount.X, 3);
        Assert.Equal(20, def.WorkpieceMount.Y, 3);
        Assert.Equal(30, def.WorkpieceMount.Z, 3);
    }
}
