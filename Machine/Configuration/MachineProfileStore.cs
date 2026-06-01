using System.IO;
using System.Text.Json;
using CNCSS.Machine.Model;
using CNCSS.Vis;

namespace CNCSS.Machine.Configuration
{
    /// <summary>Persists portable machine profiles (machine.json + STL) under Documents\CNCSS\Machines.</summary>
    public sealed class MachineProfileStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly string _rootDirectory;

        public MachineProfileStore(string? rootDirectory = null)
        {
            _rootDirectory = rootDirectory ?? GetDefaultRootDirectory();
            if (rootDirectory == null)
            {
                MigrateLegacyProfilesIfNeeded();
            }
        }

        public string RootDirectory => _rootDirectory;

        public static string GetDefaultRootDirectory() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "CNCSS",
                "Machines");

        public static string GetLegacyRootDirectory() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CNCSS",
                "Machines");

        public string GetProfileDirectory(string profileId) =>
            Path.Combine(_rootDirectory, SanitizeProfileId(profileId));

        public string GetProfileFilePath(string profileId) =>
            Path.Combine(GetProfileDirectory(profileId), "machine.json");

        public string GetActiveProfilePointerPath() =>
            Path.Combine(_rootDirectory, "active.txt");

        public IReadOnlyList<string> ListProfileIds()
        {
            EnsureRoot();
            if (!Directory.Exists(_rootDirectory))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateDirectories(_rootDirectory)
                .Select(Path.GetFileName)
                .Where(id => !string.IsNullOrWhiteSpace(id) && File.Exists(GetProfileFilePath(id!)))
                .Cast<string>()
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public MachineDefinition Load(string profileId)
        {
            string path = GetProfileFilePath(profileId);
            if (!File.Exists(path))
            {
                return MachineDefinition.CreateDefault(profileId, profileId);
            }

            try
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<MachineDefinition>(json, JsonOptions);
                if (loaded == null)
                {
                    return MachineDefinition.CreateDefault(profileId, profileId);
                }

                loaded.ProfileId = profileId;
                loaded.NormalizeAfterLoad();
                RepairOrphanStlReferences(loaded, GetProfileDirectory(profileId));
                return loaded;
            }
            catch
            {
                return MachineDefinition.CreateDefault(profileId, profileId);
            }
        }

        public void Save(MachineDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            string profileId = SanitizeProfileId(definition.ProfileId);
            definition.ProfileId = profileId;

            string dir = GetProfileDirectory(profileId);
            Directory.CreateDirectory(dir);
            string path = GetProfileFilePath(profileId);
            File.WriteAllText(path, JsonSerializer.Serialize(definition, JsonOptions));
        }

        /// <summary>Copies a portable profile folder (machine.json + STL) into the library.</summary>
        public string ImportProfileDirectory(string sourceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                throw new ArgumentException("Папка профиля не указана.", nameof(sourceDirectory));
            }

            sourceDirectory = Path.GetFullPath(sourceDirectory);
            string jsonPath = Path.Combine(sourceDirectory, "machine.json");
            if (!File.Exists(jsonPath))
            {
                throw new FileNotFoundException("В выбранной папке нет machine.json.", jsonPath);
            }

            MachineDefinition definition;
            try
            {
                string json = File.ReadAllText(jsonPath);
                definition = JsonSerializer.Deserialize<MachineDefinition>(json, JsonOptions)
                    ?? throw new InvalidOperationException("machine.json пуст или повреждён.");
                definition.NormalizeAfterLoad();
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                throw new InvalidOperationException("Не удалось прочитать machine.json.", ex);
            }

            string baseId = SanitizeProfileId(definition.ProfileId);
            if (string.IsNullOrWhiteSpace(baseId))
            {
                baseId = SanitizeProfileId(Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }

            string profileId = AllocateUniqueProfileId(baseId);
            string destDir = GetProfileDirectory(profileId);
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDirectory))
            {
                string name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(destDir, name), overwrite: true);
            }

            definition.ProfileId = profileId;
            Save(definition);
            return profileId;
        }

        public void Delete(string profileId)
        {
            string dir = GetProfileDirectory(profileId);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>Deletes every profile folder and the active-profile pointer.</summary>
        public void DeleteAllProfiles()
        {
            EnsureRoot();
            foreach (string profileId in ListProfileIds().ToList())
            {
                Delete(profileId);
            }

            string activePath = GetActiveProfilePointerPath();
            if (File.Exists(activePath))
            {
                File.Delete(activePath);
            }
        }

        public string? LoadActiveProfileId()
        {
            string pointer = GetActiveProfilePointerPath();
            if (!File.Exists(pointer))
            {
                return null;
            }

            string id = File.ReadAllText(pointer).Trim();
            return string.IsNullOrWhiteSpace(id) ? null : SanitizeProfileId(id);
        }

        public void SaveActiveProfileId(string profileId)
        {
            EnsureRoot();
            File.WriteAllText(GetActiveProfilePointerPath(), SanitizeProfileId(profileId));
        }

        public string CopyStlIntoProfile(string profileId, string sourcePath, MachineNodeKind kind) =>
            CopyStlIntoProfile(profileId, sourcePath, kind switch
            {
                MachineNodeKind.Base => "base",
                MachineNodeKind.Table => "table",
                MachineNodeKind.Spindle => "spindle",
                _ => "part"
            });

        public string CopyStlIntoProfile(string profileId, string sourcePath, string nodeFileStem)
        {
            string ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(ext))
            {
                ext = ".stl";
            }

            string safeStem = SanitizeProfileId(nodeFileStem);
            string fileName = safeStem + ext;
            string destDir = GetProfileDirectory(SanitizeProfileId(profileId));
            Directory.CreateDirectory(destDir);
            string destPath = Path.Combine(destDir, fileName);
            File.Copy(sourcePath, destPath, overwrite: true);
            return fileName;
        }

        public string? ResolveStlFullPath(string profileId, string stlFileName)
        {
            if (string.IsNullOrWhiteSpace(stlFileName))
            {
                return null;
            }

            string path = Path.Combine(GetProfileDirectory(profileId), stlFileName);
            return File.Exists(path) ? path : null;
        }

        public void EnsureDefaultProfileExists()
        {
            EnsureRoot();
            const string defaultId = "default";
            if (!File.Exists(GetProfileFilePath(defaultId)))
            {
                Save(MachineDefinition.CreateDefault(defaultId, "Станок по умолчанию"));
            }

            if (LoadActiveProfileId() == null)
            {
                SaveActiveProfileId(defaultId);
            }
        }

        private void EnsureRoot() => Directory.CreateDirectory(_rootDirectory);

        private void MigrateLegacyProfilesIfNeeded()
        {
            string legacyRoot = GetLegacyRootDirectory();
            if (!Directory.Exists(legacyRoot))
            {
                return;
            }

            EnsureRoot();
            if (ListProfileIds().Count > 0)
            {
                return;
            }

            foreach (string legacyDir in Directory.EnumerateDirectories(legacyRoot))
            {
                string profileId = Path.GetFileName(legacyDir);
                if (string.IsNullOrWhiteSpace(profileId) || !File.Exists(Path.Combine(legacyDir, "machine.json")))
                {
                    continue;
                }

                string destDir = GetProfileDirectory(profileId);
                if (Directory.Exists(destDir))
                {
                    continue;
                }

                CopyDirectoryFiles(legacyDir, destDir);
            }

            string legacyActive = Path.Combine(legacyRoot, "active.txt");
            string activePath = GetActiveProfilePointerPath();
            if (File.Exists(legacyActive) && !File.Exists(activePath))
            {
                File.Copy(legacyActive, activePath, overwrite: true);
            }
        }

        private static void CopyDirectoryFiles(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            }
        }

        private string AllocateUniqueProfileId(string baseId)
        {
            if (!Directory.Exists(GetProfileDirectory(baseId)))
            {
                return baseId;
            }

            for (int i = 2; i < 10_000; i++)
            {
                string candidate = $"{baseId}_{i}";
                if (!Directory.Exists(GetProfileDirectory(candidate)))
                {
                    return candidate;
                }
            }

            return baseId + "_" + Guid.NewGuid().ToString("N")[..8];
        }

        private static string SanitizeProfileId(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return "default";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                profileId = profileId.Replace(c, '_');
            }

            return profileId.Trim();
        }

        /// <summary>Profiles saved before a failed STEP import may have STL files but empty <see cref="MachineNodeDefinition.StlFileName"/>.</summary>
        private static void RepairOrphanStlReferences(MachineDefinition definition, string profileDirectory)
        {
            if (!Directory.Exists(profileDirectory))
            {
                return;
            }

            RepairNodeStl(definition.Base, profileDirectory, "base.stl");
            RepairNodeStl(definition.Table, profileDirectory, "table.stl");
            RepairNodeStl(definition.Spindle, profileDirectory, "spindle.stl");

            foreach (MachineExtraNodeDefinition extra in definition.ExtraNodes)
            {
                if (!string.IsNullOrWhiteSpace(extra.StlFileName))
                {
                    continue;
                }

                string? match = Directory
                    .GetFiles(profileDirectory, extra.Id + ".*")
                    .FirstOrDefault(MeshImportFormats.IsSupported);
                if (match != null)
                {
                    extra.StlFileName = Path.GetFileName(match);
                }
            }
        }

        private static void RepairNodeStl(MachineNodeDefinition node, string profileDirectory, string expectedFileName)
        {
            if (!string.IsNullOrWhiteSpace(node.StlFileName))
            {
                return;
            }

            string path = Path.Combine(profileDirectory, expectedFileName);
            if (File.Exists(path))
            {
                node.StlFileName = expectedFileName;
            }
        }
    }
}
