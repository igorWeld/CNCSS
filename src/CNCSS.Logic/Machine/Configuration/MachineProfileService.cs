using System.IO;
using CNCSS.Machine.Core;
using CNCSS.Machine.Model;
using CNCSS.Vis;

namespace CNCSS.Machine.Configuration
{
    /// <summary>Active machine profile and notifications for UI/visual layers.</summary>
    public sealed class MachineProfileService
    {
        private readonly MachineProfileStore _store;
        private MachineDefinition _active;

        public MachineProfileService(MachineProfileStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _store.EnsureDefaultProfileExists();
            string activeId = _store.LoadActiveProfileId() ?? "default";
            _active = _store.Load(activeId);
        }

        public event Action<MachineDefinition>? ActiveProfileChanged;

        public MachineDefinition ActiveProfile => _active;

        public MachineProfileStore Store => _store;

        public IReadOnlyList<string> ListProfileIds() => _store.ListProfileIds();

        public void SetActiveProfile(string profileId)
        {
            profileId = _store.Load(profileId).ProfileId;
            _active = _store.Load(profileId);
            _store.SaveActiveProfileId(profileId);
            ActiveProfileChanged?.Invoke(_active);
        }

        public void SaveProfile(MachineDefinition definition, bool makeActive = true)
        {
            ArgumentNullException.ThrowIfNull(definition);
            _store.Save(definition);
            if (makeActive)
            {
                SetActiveProfile(definition.ProfileId);
            }
        }

        public void DeleteProfile(string profileId)
        {
            _store.Delete(profileId);
            if (string.Equals(_active.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                _store.EnsureDefaultProfileExists();
                SetActiveProfile("default");
            }
        }

        public string ImportProfileDirectory(string sourceDirectory, bool makeActive = true)
        {
            string profileId = _store.ImportProfileDirectory(sourceDirectory);
            if (makeActive)
            {
                SetActiveProfile(profileId);
            }

            return profileId;
        }

        public void SaveActiveProfileAsFactoryDefault()
        {
            SaveProfile(_active.Clone(), makeActive: true);
            _store.SaveFactorySnapshotFromProfile(_active.ProfileId);
        }

        public bool HasUserFactorySnapshot() => _store.HasFactorySnapshot();

        public void ResetToFactoryDefault(StlMeshLoader stlLoader)
        {
            ArgumentNullException.ThrowIfNull(stlLoader);
            if (_store.HasFactorySnapshot())
            {
                _store.RestoreFactorySnapshotAsDefault();
                SetActiveProfile("default");
                return;
            }

            if (File.Exists(MachineProfileBootstrap.GetBundledStepPath()))
            {
                RecreateDefaultFromBundledAssembly(stlLoader);
                return;
            }

            var factory = MachineDefinition.CreateDefault("default", "Станок по умолчанию");
            _store.Save(factory);
            SetActiveProfile("default");
        }

        /// <summary>Removes all profiles and rebuilds <c>default</c> from bundled STEP (base → table → spindle).</summary>
        public void RecreateDefaultFromBundledAssembly(StlMeshLoader stlLoader)
        {
            MachineProfileBootstrap.ApplyBundledDefaultMachine(this, stlLoader);
        }

        public Vmc3AxisKinematicsModel CreateKinematicsModel() => _active.ToKinematicsModel();
    }
}
