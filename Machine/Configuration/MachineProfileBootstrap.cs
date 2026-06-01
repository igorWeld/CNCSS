using System.IO;
using CNCSS.Machine.Model;
using CNCSS.Vis;

namespace CNCSS.Machine.Configuration
{
    /// <summary>Recreates the default machine profile and optional STEP assembly from shipped assets.</summary>
    public static class MachineProfileBootstrap
    {
        public const string DefaultProfileId = "default";
        public const string DefaultStepAssetFileName = "Default_Maschine.stp";

        public static string GetBundledStepPath() =>
            Path.Combine(AppContext.BaseDirectory, "Assets", DefaultStepAssetFileName);

        /// <summary>Removes all saved profiles and creates a fresh default profile with the bundled STEP assembly.</summary>
        public static MachineDefinition ApplyBundledDefaultMachine(StlMeshLoader stlLoader)
        {
            ArgumentNullException.ThrowIfNull(stlLoader);
            string stepPath = GetBundledStepPath();
            if (!File.Exists(stepPath))
            {
                throw new FileNotFoundException(
                    "Не найден файл сборки по умолчанию. Ожидается: " + stepPath,
                    stepPath);
            }

            var store = new MachineProfileStore();
            store.DeleteAllProfiles();

            var definition = MachineDefinition.CreateDefault(DefaultProfileId, "Станок по умолчанию");
            MachineProfileAssemblyImporter.ImportStepAssembly(store, definition, stepPath, stlLoader);
            definition = store.Load(DefaultProfileId);
            ApplyBundledDefaultMcsMarker(definition);
            store.Save(definition);
            store.SaveActiveProfileId(DefaultProfileId);
            return store.Load(DefaultProfileId);
        }

        public static void ApplyBundledDefaultMachine(MachineProfileService service, StlMeshLoader stlLoader)
        {
            ArgumentNullException.ThrowIfNull(service);
            var store = service.Store;
            store.DeleteAllProfiles();

            var definition = MachineDefinition.CreateDefault(DefaultProfileId, "Станок по умолчанию");
            MachineProfileAssemblyImporter.ImportStepAssembly(store, definition, GetBundledStepPath(), stlLoader);
            definition = service.Store.Load(DefaultProfileId);
            ApplyBundledDefaultMcsMarker(definition);
            service.Store.Save(definition);
            service.SetActiveProfile(DefaultProfileId);
        }

        /// <summary>MCS уже задан в центр сборки при импорте STEP; здесь только нормализация.</summary>
        public static void ApplyBundledDefaultMcsMarker(MachineDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.NormalizeAfterLoad();
        }
    }
}
