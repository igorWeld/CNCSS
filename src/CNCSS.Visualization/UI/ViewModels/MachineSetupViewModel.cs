using System.Collections.ObjectModel;
using System.Windows.Media.Media3D;
using CNCSS.Machine.Configuration;
using CNCSS.Machine.Model;
using CNCSS.Vis;

namespace CNCSS.UI.ViewModels
{
    public sealed class MachineSetupViewModel : BaseViewModel
    {
        private bool _suppressProfileSelectionLoad;
        private readonly MachineProfileService _profileService;
        private MachineDefinition _draft = MachineDefinition.CreateDefault();
        private string _displayName = "Станок по умолчанию";
        private string _profileId = "default";
        private string _selectedProfileId = "default";
        private string _validationSummary = string.Empty;
        private double _previewX;
        private double _previewY;
        private double _previewZ;
        private double _axisXMin = -300;
        private double _axisXMax = 300;
        private double _axisXHome;
        private double _axisYMin = -200;
        private double _axisYMax = 200;
        private double _axisYHome;
        private double _axisZMin = -500;
        private double _axisZMax;
        private double _axisZHome;
        private string _toolMountNodeId = MachineNodeIds.Spindle;
        private double _toolMountX;
        private double _toolMountY;
        private double _toolMountZ;
        private string _toolMountSummary = "TCP не задан";
        private string _toolMountPickStatus = "Включите режим и кликните по грани на выбранном узле.";
        private double _workpieceMountX;
        private double _workpieceMountY;
        private double _workpieceMountZ;
        private double _fixtureHeightMm;
        private string _workpieceMountSummary = "Плоскость не задана — по центру стола, верх модели.";
        private string _workpieceMountPickStatus = "Включите режим и кликните по грани стола.";
        private string _statusMessage = string.Empty;
        private bool _previewUseWcsCoordinateSystem = true;
        private double _previewWcsG54X;
        private double _previewWcsG54Y;
        private double _previewWcsG54Z;

        public MachineSetupViewModel(MachineProfileService profileService)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            BaseNode = new MachineNodeEditorViewModel(MachineNodeKind.Base);
            TableNode = new MachineNodeEditorViewModel(MachineNodeKind.Table);
            SpindleNode = new MachineNodeEditorViewModel(MachineNodeKind.Spindle);
            ProfileIds = new ObservableCollection<string>();
            MachineLibrary = new ObservableCollection<MachineProfileListItem>();
            ParentOptions = new ObservableCollection<NodeParentOption>();
            ExtraNodes = new ObservableCollection<MachineExtraNodeEditorViewModel>();
            PreviewNodeOptions = new ObservableCollection<NodePickerItem>();
            NodeTree = new ObservableCollection<NodeTreeItem>();
            ReloadProfileList();
            string activeId = _profileService.ActiveProfile.ProfileId;
            LoadProfile(activeId);
            SelectProfileInList(activeId);
        }

        public MachineNodeEditorViewModel BaseNode { get; }
        public MachineNodeEditorViewModel TableNode { get; }
        public MachineNodeEditorViewModel SpindleNode { get; }
        public ObservableCollection<string> ProfileIds { get; }
        public ObservableCollection<MachineProfileListItem> MachineLibrary { get; }
        public ObservableCollection<MachineExtraNodeEditorViewModel> ExtraNodes { get; }
        public ObservableCollection<NodeParentOption> ParentOptions { get; }
        public ObservableCollection<NodePickerItem> PreviewNodeOptions { get; }

        public ObservableCollection<NodeTreeItem> NodeTree { get; }

        private string _selectedNodeId = MachineNodeIds.Table;

        public string SelectedNodeId
        {
            get => _selectedNodeId;
            set
            {
                if (SetProperty(ref _selectedNodeId, value))
                {
                    OnPropertyChanged(nameof(SelectedNodeDisplayName));
                    OnPropertyChanged(nameof(IsExtraNodeSelected));
                    OnPropertyChanged(nameof(IsTableNodeSelected));
                    OnPropertyChanged(nameof(IsSpindleNodeSelected));
                    OnPropertyChanged(nameof(IsBaseNodeSelected));
                    OnPropertyChanged(nameof(IsSelectedNodeRenamable));
                    OnPropertyChanged(nameof(SelectedExtraDisplayName));
                }
            }
        }

        public string SelectedNodeDisplayName =>
            PreviewNodeOptions.FirstOrDefault(n => string.Equals(n.Id, SelectedNodeId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? SelectedNodeId;

        public bool IsExtraNodeSelected => TryGetExtraEditor(SelectedNodeId) != null;

        public bool IsTableNodeSelected =>
            string.Equals(SelectedNodeId, MachineNodeIds.Table, StringComparison.OrdinalIgnoreCase);

        public bool IsSpindleNodeSelected =>
            string.Equals(SelectedNodeId, MachineNodeIds.Spindle, StringComparison.OrdinalIgnoreCase);

        public bool IsBaseNodeSelected =>
            string.Equals(SelectedNodeId, MachineNodeIds.Base, StringComparison.OrdinalIgnoreCase);


        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (SetProperty(ref _displayName, value))
                {
                    SyncMachineLibraryDisplayName();
                }
            }
        }

        public bool IsSelectedNodeRenamable => IsExtraNodeSelected;

        public string? SelectedExtraDisplayName
        {
            get => TryGetExtraEditor(SelectedNodeId)?.DisplayName;
            set
            {
                if (value == null || TryGetExtraEditor(SelectedNodeId) is not MachineExtraNodeEditorViewModel extra)
                {
                    return;
                }

                extra.DisplayName = value.Trim();
                RebuildNodeCatalog();
                OnPropertyChanged(nameof(SelectedNodeDisplayName));
            }
        }

        public string ProfileId
        {
            get => _profileId;
            set => SetProperty(ref _profileId, value);
        }

        public string SelectedProfileId
        {
            get => _selectedProfileId;
            set
            {
                if (!SetProperty(ref _selectedProfileId, value) || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                if (!_suppressProfileSelectionLoad)
                {
                    LoadProfile(value);
                }
            }
        }

        /// <summary>Increments when profile content is loaded into the UI (3D preview should rebuild).</summary>
        public int ProfileContentRevision { get; private set; }

        public string ValidationSummary
        {
            get => _validationSummary;
            set => SetProperty(ref _validationSummary, value);
        }

        public double PreviewX
        {
            get => _previewX;
            set
            {
                SetProperty(ref _previewX, value);
            }
        }

        public double PreviewY
        {
            get => _previewY;
            set
            {
                SetProperty(ref _previewY, value);
            }
        }

        public double PreviewZ
        {
            get => _previewZ;
            set
            {
                SetProperty(ref _previewZ, value);
            }
        }

        public double AxisXMin { get => _axisXMin; set { if (SetProperty(ref _axisXMin, value)) SyncPreviewSliderLimits(); } }
        public double AxisXMax { get => _axisXMax; set { if (SetProperty(ref _axisXMax, value)) SyncPreviewSliderLimits(); } }
        public double AxisXHome { get => _axisXHome; set => SetProperty(ref _axisXHome, value); }
        public double AxisYMin { get => _axisYMin; set { if (SetProperty(ref _axisYMin, value)) SyncPreviewSliderLimits(); } }
        public double AxisYMax { get => _axisYMax; set { if (SetProperty(ref _axisYMax, value)) SyncPreviewSliderLimits(); } }
        public double AxisYHome { get => _axisYHome; set => SetProperty(ref _axisYHome, value); }
        public double AxisZMin { get => _axisZMin; set { if (SetProperty(ref _axisZMin, value)) SyncPreviewSliderLimits(); } }
        public double AxisZMax { get => _axisZMax; set { if (SetProperty(ref _axisZMax, value)) SyncPreviewSliderLimits(); } }
        public double AxisZHome { get => _axisZHome; set => SetProperty(ref _axisZHome, value); }

        /// <summary>Slider limits (MCS mm); updated when axis fields commit, not on each keystroke.</summary>
        public double PreviewSliderXMinMcs { get => _previewSliderXMinMcs; private set => SetProperty(ref _previewSliderXMinMcs, value); }
        public double PreviewSliderXMaxMcs { get => _previewSliderXMaxMcs; private set => SetProperty(ref _previewSliderXMaxMcs, value); }
        public double PreviewSliderYMinMcs { get => _previewSliderYMinMcs; private set => SetProperty(ref _previewSliderYMinMcs, value); }
        public double PreviewSliderYMaxMcs { get => _previewSliderYMaxMcs; private set => SetProperty(ref _previewSliderYMaxMcs, value); }
        public double PreviewSliderZMinMcs { get => _previewSliderZMinMcs; private set => SetProperty(ref _previewSliderZMinMcs, value); }
        public double PreviewSliderZMaxMcs { get => _previewSliderZMaxMcs; private set => SetProperty(ref _previewSliderZMaxMcs, value); }

        private double _previewSliderXMinMcs = -300;
        private double _previewSliderXMaxMcs = 300;
        private double _previewSliderYMinMcs = -200;
        private double _previewSliderYMaxMcs = 200;
        private double _previewSliderZMinMcs = -500;
        private double _previewSliderZMaxMcs;

        // MCS-relative UI helpers: show preview/limits relative to user-defined MCS zero.
        private double McsShiftX => _draft.McsZeroOffset?.X ?? 0;
        private double McsShiftY => _draft.McsZeroOffset?.Y ?? 0;
        private double McsShiftZ => _draft.McsZeroOffset?.Z ?? 0;

        public double PreviewXMcs
        {
            get => PreviewX - McsShiftX;
            set => PreviewX = ClampPreviewMcs('X', value) + McsShiftX;
        }
        public double PreviewYMcs
        {
            get => PreviewY - McsShiftY;
            set => PreviewY = ClampPreviewMcs('Y', value) + McsShiftY;
        }
        public double PreviewZMcs
        {
            get => PreviewZ - McsShiftZ;
            set => PreviewZ = ClampPreviewMcs('Z', value) + McsShiftZ;
        }

        public double AxisXMinMcs { get => AxisXMin - McsShiftX; set => AxisXMin = value + McsShiftX; }
        public double AxisXMaxMcs { get => AxisXMax - McsShiftX; set => AxisXMax = value + McsShiftX; }
        public double AxisXHomeMcs { get => AxisXHome - McsShiftX; set => SetAxisHomeMcs('X', value); }
        public double AxisYMinMcs { get => AxisYMin - McsShiftY; set => AxisYMin = value + McsShiftY; }
        public double AxisYMaxMcs { get => AxisYMax - McsShiftY; set => AxisYMax = value + McsShiftY; }
        public double AxisYHomeMcs { get => AxisYHome - McsShiftY; set => SetAxisHomeMcs('Y', value); }
        public double AxisZMinMcs { get => AxisZMin - McsShiftZ; set => AxisZMin = value + McsShiftZ; }
        public double AxisZMaxMcs { get => AxisZMax - McsShiftZ; set => AxisZMax = value + McsShiftZ; }
        public double AxisZHomeMcs { get => AxisZHome - McsShiftZ; set => SetAxisHomeMcs('Z', value); }

        private void SetAxisHomeMcs(char axis, double mcsValue)
        {
            (double minMcs, double maxMcs) = axis switch
            {
                'X' => (AxisXMinMcs, AxisXMaxMcs),
                'Y' => (AxisYMinMcs, AxisYMaxMcs),
                'Z' => (AxisZMinMcs, AxisZMaxMcs),
                _ => (0, 0)
            };
            double lo = Math.Min(minMcs, maxMcs);
            double hi = Math.Max(minMcs, maxMcs);
            double clamped = Math.Clamp(mcsValue, lo, hi);

            switch (axis)
            {
                case 'X': AxisXHome = clamped + McsShiftX; break;
                case 'Y': AxisYHome = clamped + McsShiftY; break;
                case 'Z': AxisZHome = clamped + McsShiftZ; break;
            }
        }

        public string ToolMountNodeId
        {
            get => _toolMountNodeId;
            set
            {
                if (SetProperty(ref _toolMountNodeId, value))
                {
                    UpdateToolMountSummary();
                }
            }
        }

        public double ToolMountX
        {
            get => _toolMountX;
            set
            {
                if (SetProperty(ref _toolMountX, value))
                {
                    UpdateToolMountSummary();
                }
            }
        }

        public double ToolMountY
        {
            get => _toolMountY;
            set
            {
                if (SetProperty(ref _toolMountY, value))
                {
                    UpdateToolMountSummary();
                }
            }
        }

        public double ToolMountZ
        {
            get => _toolMountZ;
            set
            {
                if (SetProperty(ref _toolMountZ, value))
                {
                    UpdateToolMountSummary();
                }
            }
        }

        public string ToolMountSummary
        {
            get => _toolMountSummary;
            private set => SetProperty(ref _toolMountSummary, value);
        }

        public string ToolMountPickStatus
        {
            get => _toolMountPickStatus;
            set => SetProperty(ref _toolMountPickStatus, value);
        }

        public double WorkpieceMountX
        {
            get => _workpieceMountX;
            set
            {
                if (SetProperty(ref _workpieceMountX, value))
                {
                    UpdateWorkpieceMountSummary();
                }
            }
        }

        public double WorkpieceMountY
        {
            get => _workpieceMountY;
            set
            {
                if (SetProperty(ref _workpieceMountY, value))
                {
                    UpdateWorkpieceMountSummary();
                }
            }
        }

        public double WorkpieceMountZ
        {
            get => _workpieceMountZ;
            set
            {
                if (SetProperty(ref _workpieceMountZ, value))
                {
                    UpdateWorkpieceMountSummary();
                }
            }
        }

        public double FixtureHeightMm
        {
            get => _fixtureHeightMm;
            set
            {
                if (SetProperty(ref _fixtureHeightMm, value))
                {
                    UpdateWorkpieceMountSummary();
                }
            }
        }

        public string WorkpieceMountSummary
        {
            get => _workpieceMountSummary;
            private set => SetProperty(ref _workpieceMountSummary, value);
        }

        public string WorkpieceMountPickStatus
        {
            get => _workpieceMountPickStatus;
            set => SetProperty(ref _workpieceMountPickStatus, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool PreviewUseWcsCoordinateSystem
        {
            get => _previewUseWcsCoordinateSystem;
            set => SetProperty(ref _previewUseWcsCoordinateSystem, value);
        }

        public double PreviewWcsG54X
        {
            get => _previewWcsG54X;
            set => SetProperty(ref _previewWcsG54X, value);
        }

        public double PreviewWcsG54Y
        {
            get => _previewWcsG54Y;
            set => SetProperty(ref _previewWcsG54Y, value);
        }

        public double PreviewWcsG54Z
        {
            get => _previewWcsG54Z;
            set => SetProperty(ref _previewWcsG54Z, value);
        }

        public MachineDefinition BuildDraftFromUi()
        {
            ApplyUiEditorsToDraft();
            return _draft;
        }

        /// <summary>Записывает поля редакторов узлов в <see cref="_draft"/> без сброса MCS/кинематики из превью.</summary>
        public void ApplyUiEditorsToDraft()
        {
            _draft.DisplayName = DisplayName.Trim();
            _draft.ProfileId = ProfileId.Trim();
            BaseNode.ApplyTo(_draft.Base);
            TableNode.ApplyTo(_draft.Table);
            SpindleNode.ApplyTo(_draft.Spindle);
            _draft.Base.StlFileName = BaseNode.GetStlFileName();
            _draft.Table.StlFileName = TableNode.GetStlFileName();
            _draft.Spindle.StlFileName = SpindleNode.GetStlFileName();

            var extras = new List<MachineExtraNodeDefinition>();
            foreach (MachineExtraNodeEditorViewModel vm in ExtraNodes)
            {
                var model = _draft.ExtraNodes.FirstOrDefault(n => string.Equals(n.Id, vm.Id, StringComparison.OrdinalIgnoreCase))
                    ?? new MachineExtraNodeDefinition { Id = vm.Id };
                vm.ApplyTo(model);
                model.StlFileName = vm.GetStlFileName();
                extras.Add(model);
            }

            _draft.ExtraNodes = extras;
            _draft.ToolMountNodeId = ToolMountNodeId;
            _draft.ToolMount = new MachineGeometryPoint { X = ToolMountX, Y = ToolMountY, Z = ToolMountZ };
            _draft.ToolMountIsNodeLocal = true;
            _draft.WorkpieceMount = new MachineGeometryPoint
            {
                X = WorkpieceMountX,
                Y = WorkpieceMountY,
                Z = WorkpieceMountZ
            };
            _draft.FixtureHeightMm = FixtureHeightMm;
            _draft.DefaultWorkOffsetG54 = new MachineGeometryPoint
            {
                X = PreviewWcsG54X,
                Y = PreviewWcsG54Y,
                Z = PreviewWcsG54Z
            };
            ApplyAxesToDraft();
            _draft.SyncHomePositionFromAxes();
        }

        /// <summary>Применяет профиль из 3D-превью (после переноса MCS и т.п.) в черновик и UI.</summary>
        public void ApplyDefinitionFromPreview(MachineDefinition previewDefinition)
        {
            _draft = previewDefinition.Clone();
            LoadNodesAndMountsFromDraft();
            SyncPreviewSliderLimits();
            NotifyAxisPreviewAndSliderBindings();
            // Не вызывать FinishProfileUiLoad: ProfileContentRevision → RefreshPreviewFromDraft → … → переполнение стека.
        }

        public MachineDefinition GetDraftSnapshot() => BuildDraftFromUi().Clone();

        public bool TryValidate(out IReadOnlyList<string> errors)
        {
            var draft = BuildDraftFromUi();
            errors = draft.Validate();
            ValidationSummary = errors.Count == 0
                ? "Параметры корректны."
                : string.Join(Environment.NewLine, errors);
            return errors.Count == 0;
        }

        public void ReloadProfileList()
        {
            ProfileIds.Clear();
            MachineLibrary.Clear();
            foreach (string id in _profileService.ListProfileIds())
            {
                ProfileIds.Add(id);
                MachineDefinition profile = _profileService.Store.Load(id);
                MachineLibrary.Add(new MachineProfileListItem(id, profile.DisplayName));
            }
        }

        private void SyncMachineLibraryDisplayName()
        {
            foreach (MachineProfileListItem item in MachineLibrary)
            {
                if (string.Equals(item.Id, ProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    item.DisplayName = DisplayName;
                    break;
                }
            }
        }

        public bool TrySetBuiltInMotion(MachineNodeKind kind, char axis, bool enabled, out string error)
        {
            error = string.Empty;
            MachineNodeEditorViewModel editor = kind switch
            {
                MachineNodeKind.Table => TableNode,
                MachineNodeKind.Spindle => SpindleNode,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };

            string nodeId = kind == MachineNodeKind.Table ? MachineNodeIds.Table : MachineNodeIds.Spindle;
            if (enabled)
            {
                string? ownerId = FindProgramAxisOwner(axis, excludeNodeId: nodeId);
                if (ownerId != null)
                {
                    if ((ownerId == MachineNodeIds.Table && kind == MachineNodeKind.Spindle)
                        || (ownerId == MachineNodeIds.Spindle && kind == MachineNodeKind.Table))
                    {
                        error = $"Ось {char.ToUpperInvariant(axis)} уже назначена узлу «{GetNodeDisplayName(ownerId)}». " +
                                $"Стол и шпиндель не могут перемещаться по одной оси.";
                    }
                    else
                    {
                        error = $"Ось {char.ToUpperInvariant(axis)} уже назначена узлу «{GetNodeDisplayName(ownerId)}».";
                    }

                    return false;
                }
            }

            switch (char.ToUpperInvariant(axis))
            {
                case 'X': editor.MotionX = enabled; break;
                case 'Y': editor.MotionY = enabled; break;
                case 'Z': editor.MotionZ = enabled; break;
                default:
                    error = $"Неизвестная ось: {axis}";
                    return false;
            }

            return true;
        }

        public bool TrySetExtraMotion(string extraNodeId, char axis, bool enabled, out string error)
        {
            error = string.Empty;
            if (TryGetExtraEditor(extraNodeId) is not MachineExtraNodeEditorViewModel extra)
            {
                error = "Узел не найден.";
                return false;
            }

            if (enabled)
            {
                string? ownerId = FindProgramAxisOwner(axis, excludeNodeId: extraNodeId);
                if (ownerId != null)
                {
                    error = $"Ось {char.ToUpperInvariant(axis)} уже назначена узлу «{GetNodeDisplayName(ownerId)}».";
                    return false;
                }
            }

            switch (char.ToUpperInvariant(axis))
            {
                case 'X': extra.MotionX = enabled; break;
                case 'Y': extra.MotionY = enabled; break;
                case 'Z': extra.MotionZ = enabled; break;
                default:
                    error = $"Неизвестная ось: {axis}";
                    return false;
            }

            return true;
        }

        private string? FindProgramAxisOwner(char axis, string? excludeNodeId = null)
        {
            if (TableNode.MotionX && axis is 'X' or 'x' && !IsExcluded(MachineNodeIds.Table, excludeNodeId))
            {
                return MachineNodeIds.Table;
            }

            if (TableNode.MotionY && axis is 'Y' or 'y' && !IsExcluded(MachineNodeIds.Table, excludeNodeId))
            {
                return MachineNodeIds.Table;
            }

            if (TableNode.MotionZ && axis is 'Z' or 'z' && !IsExcluded(MachineNodeIds.Table, excludeNodeId))
            {
                return MachineNodeIds.Table;
            }

            if (SpindleNode.MotionX && axis is 'X' or 'x' && !IsExcluded(MachineNodeIds.Spindle, excludeNodeId))
            {
                return MachineNodeIds.Spindle;
            }

            if (SpindleNode.MotionY && axis is 'Y' or 'y' && !IsExcluded(MachineNodeIds.Spindle, excludeNodeId))
            {
                return MachineNodeIds.Spindle;
            }

            if (SpindleNode.MotionZ && axis is 'Z' or 'z' && !IsExcluded(MachineNodeIds.Spindle, excludeNodeId))
            {
                return MachineNodeIds.Spindle;
            }

            foreach (MachineExtraNodeEditorViewModel extra in ExtraNodes)
            {
                if (extra.MotionLink == MachineMotionLink.Fixed)
                {
                    continue;
                }

                if (extra.MotionX && axis is 'X' or 'x' && !IsExcluded(extra.Id, excludeNodeId))
                {
                    return extra.Id;
                }

                if (extra.MotionY && axis is 'Y' or 'y' && !IsExcluded(extra.Id, excludeNodeId))
                {
                    return extra.Id;
                }

                if (extra.MotionZ && axis is 'Z' or 'z' && !IsExcluded(extra.Id, excludeNodeId))
                {
                    return extra.Id;
                }
            }

            return null;
        }

        private static bool IsExcluded(string nodeId, string? excludeNodeId) =>
            excludeNodeId != null && string.Equals(nodeId, excludeNodeId, StringComparison.OrdinalIgnoreCase);

        private string GetNodeDisplayName(string nodeId) =>
            PreviewNodeOptions.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? nodeId;

        public void LoadProfile(string profileId)
        {
            _draft = _profileService.Store.Load(profileId).Clone();
            DisplayName = _draft.DisplayName;
            ProfileId = _draft.ProfileId;
            LoadNodesAndMountsFromDraft();
            ValidationSummary = string.Empty;
            RebuildNodeCatalog();
            UpdateToolMountSummary();
            FinishProfileUiLoad();
        }

        private void LoadNodesAndMountsFromDraft()
        {
            BaseNode.LoadFrom(_draft.Base);
            TableNode.LoadFrom(_draft.Table);
            SpindleNode.LoadFrom(_draft.Spindle);
            LoadExtraNodesFromDraft();
            RefreshNodePositionMcsDisplay();
            LoadAxesFromDraft();
            LoadToolMountFromDraft();
            LoadWorkpieceMountFromDraft();
            LoadWorkOffsetFromDraft();
        }

        /// <summary>Обновляет поля «Положение» (координаты в MCS) после переноса MCS.</summary>
        public void RefreshNodePositionMcsDisplay()
        {
            double px = PreviewX;
            double py = PreviewY;
            double pz = PreviewZ;
            BaseNode.SetPositionMcsDisplay(
                MachineKinematics.GetNodeMeshOriginAssemblyMcs(_draft, MachineNodeIds.Base, px, py, pz));
            TableNode.SetPositionMcsDisplay(
                MachineKinematics.GetNodeMeshOriginAssemblyMcs(_draft, MachineNodeIds.Table, px, py, pz));
            SpindleNode.SetPositionMcsDisplay(
                MachineKinematics.GetNodeMeshOriginAssemblyMcs(_draft, MachineNodeIds.Spindle, px, py, pz));
            foreach (MachineExtraNodeEditorViewModel extra in ExtraNodes)
            {
                extra.SetPositionMcsDisplay(
                    MachineKinematics.GetNodeMeshOriginAssemblyMcs(_draft, extra.Id, px, py, pz));
            }
        }

        private void LoadWorkOffsetFromDraft()
        {
            var wcs = _draft.DefaultWorkOffsetG54 ?? MachineGeometryPoint.Zero;
            PreviewWcsG54X = wcs.X;
            PreviewWcsG54Y = wcs.Y;
            PreviewWcsG54Z = wcs.Z;
        }

        private void FinishProfileUiLoad()
        {
            ApplyLoadedDraftToUi();
            ProfileContentRevision++;
            OnPropertyChanged(nameof(ProfileContentRevision));
        }

        private void ApplyLoadedDraftToUi()
        {
            _draft.SyncHomePositionFromAxes();
            var physicalHome = _draft.GetPhysicalHomePosition();
            PreviewX = physicalHome.X;
            PreviewY = physicalHome.Y;
            PreviewZ = physicalHome.Z;
            SyncPreviewSliderLimits();
            NotifyAxisPreviewAndSliderBindings();
        }

        private double ClampPreviewMcs(char axis, double mcsValue)
        {
            (double minMcs, double maxMcs) = axis switch
            {
                'X' => (AxisXMinMcs, AxisXMaxMcs),
                'Y' => (AxisYMinMcs, AxisYMaxMcs),
                'Z' => (AxisZMinMcs, AxisZMaxMcs),
                _ => (0, 0)
            };
            double lo = Math.Min(minMcs, maxMcs);
            double hi = Math.Max(minMcs, maxMcs);
            return Math.Clamp(mcsValue, lo, hi);
        }

        private void NotifyAxisPreviewAndSliderBindings()
        {
            OnPropertyChanged(nameof(PreviewX));
            OnPropertyChanged(nameof(PreviewY));
            OnPropertyChanged(nameof(PreviewZ));
            OnPropertyChanged(nameof(PreviewXMcs));
            OnPropertyChanged(nameof(PreviewYMcs));
            OnPropertyChanged(nameof(PreviewZMcs));
            OnPropertyChanged(nameof(AxisXMin));
            OnPropertyChanged(nameof(AxisXMax));
            OnPropertyChanged(nameof(AxisXHome));
            OnPropertyChanged(nameof(AxisYMin));
            OnPropertyChanged(nameof(AxisYMax));
            OnPropertyChanged(nameof(AxisYHome));
            OnPropertyChanged(nameof(AxisZMin));
            OnPropertyChanged(nameof(AxisZMax));
            OnPropertyChanged(nameof(AxisZHome));
            OnPropertyChanged(nameof(AxisXMinMcs));
            OnPropertyChanged(nameof(AxisXMaxMcs));
            OnPropertyChanged(nameof(AxisXHomeMcs));
            OnPropertyChanged(nameof(AxisYMinMcs));
            OnPropertyChanged(nameof(AxisYMaxMcs));
            OnPropertyChanged(nameof(AxisYHomeMcs));
            OnPropertyChanged(nameof(AxisZMinMcs));
            OnPropertyChanged(nameof(AxisZMaxMcs));
            OnPropertyChanged(nameof(AxisZHomeMcs));
            OnPropertyChanged(nameof(PreviewSliderXMinMcs));
            OnPropertyChanged(nameof(PreviewSliderXMaxMcs));
            OnPropertyChanged(nameof(PreviewSliderYMinMcs));
            OnPropertyChanged(nameof(PreviewSliderYMaxMcs));
            OnPropertyChanged(nameof(PreviewSliderZMinMcs));
            OnPropertyChanged(nameof(PreviewSliderZMaxMcs));
        }

        private void SelectProfileInList(string profileId)
        {
            _suppressProfileSelectionLoad = true;
            try
            {
                SelectedProfileId = profileId;
            }
            finally
            {
                _suppressProfileSelectionLoad = false;
            }
        }

        public string ProfilesFolderPath => _profileService.Store.RootDirectory;

        public string ImportProfileFromDirectory(string sourceDirectory, bool makeActive = true)
        {
            string profileId = _profileService.ImportProfileDirectory(sourceDirectory, makeActive);
            ReloadProfileList();
            LoadProfile(profileId);
            SelectProfileInList(profileId);
            return profileId;
        }

        public void CreateNewProfile(string newProfileId)
        {
            newProfileId = newProfileId.Trim();
            if (string.IsNullOrWhiteSpace(newProfileId))
            {
                newProfileId = "profile_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }

            _draft = MachineDefinition.CreateDefault(newProfileId, "Новый станок");
            DisplayName = _draft.DisplayName;
            ProfileId = _draft.ProfileId;
            LoadNodesAndMountsFromDraft();
            ReloadProfileList();
            if (!ProfileIds.Contains(ProfileId))
            {
                ProfileIds.Add(ProfileId);
            }

            ValidationSummary = string.Empty;
            RebuildNodeCatalog();
            UpdateToolMountSummary();
            FinishProfileUiLoad();
            SelectProfileInList(ProfileId);
        }

        public MachineExtraNodeEditorViewModel AddExtraNode(string? displayName = null)
        {
            var draft = BuildDraftFromUi();
            string name = string.IsNullOrWhiteSpace(displayName)
                ? "Доп. узел " + (draft.ExtraNodes.Count + 1)
                : displayName.Trim();
            var model = MachineExtraNodeDefinition.CreateNew(name, MachineNodeIds.Table);
            draft.ExtraNodes.Add(model);
            _draft = draft;
            RebuildNodeCatalog();
            var vm = new MachineExtraNodeEditorViewModel(model.Id, ParentOptions);
            vm.LoadFrom(model);
            ExtraNodes.Add(vm);
            foreach (MachineExtraNodeEditorViewModel other in ExtraNodes)
            {
                other.ParentOptions.Clear();
                foreach (NodeParentOption option in ParentOptions)
                {
                    if (!string.Equals(option.Id, other.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        other.ParentOptions.Add(option);
                    }
                }
            }

            return vm;
        }

        public void RemoveExtraNode(MachineExtraNodeEditorViewModel node)
        {
            ExtraNodes.Remove(node);
            var draft = BuildDraftFromUi();
            draft.ExtraNodes.RemoveAll(n => string.Equals(n.Id, node.Id, StringComparison.OrdinalIgnoreCase));
            _draft = draft;
            RebuildNodeCatalog();
        }

        public void RebuildNodeCatalog()
        {
            var modeById = NodeTree.ToDictionary(n => n.Id, n => n.DisplayMode, StringComparer.OrdinalIgnoreCase);
            var draft = BuildDraftFromUi();
            ParentOptions.Clear();
            foreach ((string id, string name) in draft.EnumerateNodeCatalog())
            {
                ParentOptions.Add(new NodeParentOption(id, name));
            }

            PreviewNodeOptions.Clear();
            NodeTree.Clear();
            void AddTree(string id, string name, bool canDelete = false)
            {
                PreviewNodeOptions.Add(new NodePickerItem(id, name));
                var item = new NodeTreeItem(id, name, canDelete);
                if (modeById.TryGetValue(id, out int mode)) item.DisplayMode = mode;
                NodeTree.Add(item);
            }

            AddTree(MachineNodeIds.Spindle, "Шпиндель");
            AddTree(MachineNodeIds.Table, "Стол");
            AddTree(MachineNodeIds.Base, "Основание");
            foreach (MachineExtraNodeDefinition extra in draft.ExtraNodes)
            {
                AddTree(extra.Id, extra.DisplayName, canDelete: true);
            }

            foreach (MachineExtraNodeEditorViewModel vm in ExtraNodes)
            {
                vm.ParentOptions.Clear();
                foreach (NodeParentOption option in ParentOptions)
                {
                    if (!string.Equals(option.Id, vm.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        vm.ParentOptions.Add(option);
                    }
                }
            }
        }

        public Task ApplyAssemblyImportAsync(
            IReadOnlyList<ComponentRoleAssignment> assignments,
            StlMeshLoader stlLoader)
        {
            _ = stlLoader;
            var assignmentList = assignments as IList<ComponentRoleAssignment> ?? assignments.ToList();
            StlAnalysisResult sharedScale = MeshAssemblyPlacement.ComputeSharedScale(
                assignmentList.Select(a => a.Component).ToList());

            var pending = new List<PendingAssemblyPart>(assignmentList.Count);
            foreach (ComponentRoleAssignment assignment in assignmentList)
            {
                string? tempPath = null;
                try
                {
                    tempPath = MeshComponentFileExporter.ExportToTempStl(assignment.Component.Model);
                    Point3D centroid = StlModelMetrics.GetCenter(assignment.Component.Model);
                    switch (assignment.Role)
                    {
                        case MachineComponentRole.Spindle:
                            CopyBuiltInAssemblyStl(MachineNodeKind.Spindle, tempPath);
                            pending.Add(new PendingAssemblyPart(
                                MachineNodeIds.Spindle,
                                MachineNodeKind.Spindle,
                                centroid));
                            break;
                        case MachineComponentRole.Base:
                            CopyBuiltInAssemblyStl(MachineNodeKind.Base, tempPath);
                            pending.Add(new PendingAssemblyPart(
                                MachineNodeIds.Base,
                                MachineNodeKind.Base,
                                centroid));
                            break;
                        case MachineComponentRole.Table:
                            CopyBuiltInAssemblyStl(MachineNodeKind.Table, tempPath);
                            pending.Add(new PendingAssemblyPart(
                                MachineNodeIds.Table,
                                MachineNodeKind.Table,
                                centroid));
                            break;
                        case MachineComponentRole.Other:
                            MachineExtraNodeEditorViewModel extra = AddExtraNode(assignment.Component.DisplayName);
                            CopyExtraAssemblyStl(extra.Id, tempPath);
                            pending.Add(new PendingAssemblyPart(extra.Id, null, centroid));
                            break;
                    }
                }
                finally
                {
                    MeshComponentFileExporter.TryDelete(tempPath);
                }
            }

            MachineDefinition draft = BuildDraftFromUi();
            IReadOnlyDictionary<string, Transform3D> transforms =
                KinematicChainSolver.SolveTransforms(draft, PreviewX, PreviewY, PreviewZ);

            foreach (PendingAssemblyPart part in pending)
            {
                MachineGeometryPoint offset = MeshAssemblyPlacement.ComputeMeshOffset(
                    transforms,
                    part.NodeId,
                    part.AssemblyCentroid,
                    sharedScale.SuggestedMeshScale);

                if (part.BuiltInKind is MachineNodeKind kind)
                {
                    MachineNodeEditorViewModel nodeVm = GetNodeEditor(kind);
                    MachineNodeDefinition nodeDef = draft.GetNode(kind);
                    ApplyAssemblyLayoutToNode(nodeVm, nodeDef, sharedScale, offset);
                    continue;
                }

                MachineExtraNodeEditorViewModel? extraVm = ExtraNodes.FirstOrDefault(n =>
                    string.Equals(n.Id, part.NodeId, StringComparison.OrdinalIgnoreCase));
                MachineExtraNodeDefinition? extraDef = draft.ExtraNodes.FirstOrDefault(n =>
                    string.Equals(n.Id, part.NodeId, StringComparison.OrdinalIgnoreCase));
                if (extraVm != null && extraDef != null)
                {
                    ApplyAssemblyLayoutToExtraNode(extraVm, extraDef, sharedScale, offset);
                }
            }

            _draft = BuildDraftFromUi();
            ProfileContentRevision++;
            OnPropertyChanged(nameof(ProfileContentRevision));
            return Task.CompletedTask;
        }

        private sealed record PendingAssemblyPart(string NodeId, MachineNodeKind? BuiltInKind, Point3D AssemblyCentroid);

        private void CopyBuiltInAssemblyStl(MachineNodeKind kind, string sourcePath)
        {
            var draft = BuildDraftFromUi();
            string fileName = _profileService.Store.CopyStlIntoProfile(draft.ProfileId, sourcePath, kind);
            MachineNodeEditorViewModel nodeVm = GetNodeEditor(kind);
            MachineNodeDefinition nodeDef = draft.GetNode(kind);
            nodeDef.StlFileName = fileName;
            nodeVm.SetStlFileName(fileName);
            _draft = draft;
        }

        private void CopyExtraAssemblyStl(string nodeId, string sourcePath)
        {
            var draft = BuildDraftFromUi();
            MachineExtraNodeDefinition? nodeDef = draft.ExtraNodes.FirstOrDefault(n =>
                string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));
            MachineExtraNodeEditorViewModel? nodeVm = ExtraNodes.FirstOrDefault(n =>
                string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));
            if (nodeDef == null || nodeVm == null)
            {
                return;
            }

            string fileName = _profileService.Store.CopyStlIntoProfile(draft.ProfileId, sourcePath, "extra_" + nodeId);
            nodeDef.StlFileName = fileName;
            nodeVm.SetStlFileName(fileName);
            _draft = draft;
        }

        private static void ApplyAssemblyLayoutToNode(
            MachineNodeEditorViewModel nodeVm,
            MachineNodeDefinition nodeDef,
            StlAnalysisResult sharedScale,
            MachineGeometryPoint meshOffset)
        {
            nodeVm.ApplyStlAnalysis(sharedScale);
            nodeVm.MeshScale = sharedScale.SuggestedMeshScale;
            nodeVm.MeshRotationX = 0;
            nodeVm.MeshRotationY = 0;
            nodeVm.MeshRotationZ = 0;
            nodeVm.OffsetX = meshOffset.X;
            nodeVm.OffsetY = meshOffset.Y;
            nodeVm.OffsetZ = meshOffset.Z;
            nodeVm.ApplyTo(nodeDef);
        }

        private static void ApplyAssemblyLayoutToExtraNode(
            MachineExtraNodeEditorViewModel nodeVm,
            MachineExtraNodeDefinition nodeDef,
            StlAnalysisResult sharedScale,
            MachineGeometryPoint meshOffset)
        {
            nodeVm.ApplyStlAnalysis(sharedScale);
            nodeVm.MeshScale = sharedScale.SuggestedMeshScale;
            nodeVm.MeshRotationX = 0;
            nodeVm.MeshRotationY = 0;
            nodeVm.MeshRotationZ = 0;
            nodeVm.OffsetX = meshOffset.X;
            nodeVm.OffsetY = meshOffset.Y;
            nodeVm.OffsetZ = meshOffset.Z;
            nodeVm.ApplyTo(nodeDef);
        }

        public async Task AssignStlAsync(MachineNodeKind kind, string sourcePath, StlMeshLoader stlLoader)
        {
            string nodeId = kind switch
            {
                MachineNodeKind.Base => MachineNodeIds.Base,
                MachineNodeKind.Spindle => MachineNodeIds.Spindle,
                _ => MachineNodeIds.Table
            };
            await AssignBuiltInStlAsync(nodeId, kind, sourcePath, stlLoader);
        }

        public async Task AssignExtraStlAsync(string nodeId, string sourcePath, StlMeshLoader stlLoader)
        {
            var draft = BuildDraftFromUi();
            MachineExtraNodeDefinition? nodeDef = draft.ExtraNodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));
            MachineExtraNodeEditorViewModel? nodeVm = ExtraNodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));
            if (nodeDef == null || nodeVm == null)
            {
                return;
            }

            string fileName = _profileService.Store.CopyStlIntoProfile(draft.ProfileId, sourcePath, "extra_" + nodeId);
            string? fullPath = _profileService.Store.ResolveStlFullPath(draft.ProfileId, fileName);
            StlAnalysisResult? analysis = fullPath != null ? await stlLoader.AnalyzeAsync(fullPath) : null;

            nodeDef.StlFileName = fileName;
            nodeVm.SetStlFileName(fileName);
            if (analysis != null)
            {
                nodeVm.ApplyStlAnalysis(analysis.Value);
                nodeVm.ApplyTo(nodeDef);
            }

            _draft = draft;
        }

        private async Task AssignBuiltInStlAsync(string nodeId, MachineNodeKind kind, string sourcePath, StlMeshLoader stlLoader)
        {
            var draft = BuildDraftFromUi();
            string fileName = _profileService.Store.CopyStlIntoProfile(draft.ProfileId, sourcePath, kind);
            string? fullPath = _profileService.Store.ResolveStlFullPath(draft.ProfileId, fileName);
            StlAnalysisResult? analysis = fullPath != null ? await stlLoader.AnalyzeAsync(fullPath) : null;

            MachineNodeEditorViewModel nodeVm = GetNodeEditor(kind);
            MachineNodeDefinition nodeDef = draft.GetNode(kind);

            nodeDef.StlFileName = fileName;
            nodeVm.SetStlFileName(fileName);
            if (analysis != null)
            {
                nodeVm.ApplyStlAnalysis(analysis.Value);
                nodeVm.ApplyTo(nodeDef);
            }

            _draft = draft;
        }

        public async Task RefreshMissingStlMetricsAsync(StlMeshLoader stlLoader)
        {
            var draft = BuildDraftFromUi();
            await RefreshNodeMetricsAsync(stlLoader, draft.ProfileId, draft.Base, BaseNode);
            await RefreshNodeMetricsAsync(stlLoader, draft.ProfileId, draft.Table, TableNode);
            await RefreshNodeMetricsAsync(stlLoader, draft.ProfileId, draft.Spindle, SpindleNode);

            foreach (MachineExtraNodeEditorViewModel vm in ExtraNodes)
            {
                MachineExtraNodeDefinition? node = draft.ExtraNodes.FirstOrDefault(n => string.Equals(n.Id, vm.Id, StringComparison.OrdinalIgnoreCase));
                if (node == null)
                {
                    continue;
                }

                await RefreshExtraMetricsAsync(stlLoader, draft.ProfileId, node, vm);
            }

            _draft = draft;
        }

        private async Task RefreshNodeMetricsAsync(
            StlMeshLoader stlLoader,
            string profileId,
            MachineNodeDefinition node,
            MachineNodeEditorViewModel nodeVm)
        {
            if (string.IsNullOrWhiteSpace(node.StlFileName) || node.StlSourceMaxExtent > 1e-9)
            {
                return;
            }

            string? fullPath = _profileService.Store.ResolveStlFullPath(profileId, node.StlFileName);
            if (fullPath == null)
            {
                return;
            }

            StlAnalysisResult? analysis = await stlLoader.AnalyzeAsync(fullPath);
            if (analysis == null)
            {
                return;
            }

            nodeVm.ApplyStlAnalysis(analysis.Value);
            nodeVm.ApplyTo(node);
        }

        private async Task RefreshExtraMetricsAsync(
            StlMeshLoader stlLoader,
            string profileId,
            MachineExtraNodeDefinition node,
            MachineExtraNodeEditorViewModel nodeVm)
        {
            if (string.IsNullOrWhiteSpace(node.StlFileName) || node.StlSourceMaxExtent > 1e-9)
            {
                return;
            }

            string? fullPath = _profileService.Store.ResolveStlFullPath(profileId, node.StlFileName);
            if (fullPath == null)
            {
                return;
            }

            StlAnalysisResult? analysis = await stlLoader.AnalyzeAsync(fullPath);
            if (analysis == null)
            {
                return;
            }

            nodeVm.ApplyStlAnalysis(analysis.Value);
            nodeVm.ApplyTo(node);
        }

        public MachineNodeEditorViewModel? TryGetBuiltInEditor(string nodeId)
        {
            if (string.Equals(nodeId, MachineNodeIds.Base, StringComparison.OrdinalIgnoreCase))
            {
                return BaseNode;
            }

            if (string.Equals(nodeId, MachineNodeIds.Table, StringComparison.OrdinalIgnoreCase))
            {
                return TableNode;
            }

            if (string.Equals(nodeId, MachineNodeIds.Spindle, StringComparison.OrdinalIgnoreCase))
            {
                return SpindleNode;
            }

            return null;
        }

        public MachineExtraNodeEditorViewModel? TryGetExtraEditor(string nodeId) =>
            ExtraNodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));

        private MachineNodeEditorViewModel GetNodeEditor(MachineNodeKind kind) => kind switch
        {
            MachineNodeKind.Base => BaseNode,
            MachineNodeKind.Table => TableNode,
            MachineNodeKind.Spindle => SpindleNode,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public void ClearStl(MachineNodeKind kind)
        {
            switch (kind)
            {
                case MachineNodeKind.Base:
                    _draft.Base.StlFileName = string.Empty;
                    BaseNode.ClearStl();
                    break;
                case MachineNodeKind.Table:
                    _draft.Table.StlFileName = string.Empty;
                    TableNode.ClearStl();
                    break;
                case MachineNodeKind.Spindle:
                    _draft.Spindle.StlFileName = string.Empty;
                    SpindleNode.ClearStl();
                    break;
            }
        }

        public void ClearExtraStl(string nodeId)
        {
            MachineExtraNodeEditorViewModel? vm = TryGetExtraEditor(nodeId);
            if (vm == null)
            {
                return;
            }

            vm.ClearStl();
            var draft = BuildDraftFromUi();
            MachineExtraNodeDefinition? node = draft.ExtraNodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));
            if (node != null)
            {
                node.StlFileName = string.Empty;
            }

            _draft = draft;
        }

        public void Save(bool makeActive)
        {
            if (!TryValidate(out _))
            {
                throw new InvalidOperationException(ValidationSummary);
            }

            ApplyUiEditorsToDraft();
            var draft = _draft.Clone();
            draft.FinalizeProfileMcsAtSceneCenter(PreviewX, PreviewY, PreviewZ);
            _profileService.SaveProfile(draft, makeActive);
            ReloadProfileList();
            LoadProfile(draft.ProfileId);
            SelectProfileInList(draft.ProfileId);
        }

        public void DeleteSelectedProfile()
        {
            if (string.Equals(SelectedProfileId, "default", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Профиль default нельзя удалить.");
            }

            _profileService.DeleteProfile(SelectedProfileId);
            ReloadProfileList();
            string activeId = _profileService.ActiveProfile.ProfileId;
            LoadProfile(activeId);
            SelectProfileInList(activeId);
        }

        public void SaveAsFactoryDefault() => _profileService.SaveActiveProfileAsFactoryDefault();

        public void ResetFactory(StlMeshLoader stlLoader) =>
            _profileService.ResetToFactoryDefault(stlLoader);

        private void LoadExtraNodesFromDraft()
        {
            ExtraNodes.Clear();
            RebuildNodeCatalog();
            foreach (MachineExtraNodeDefinition extra in _draft.ExtraNodes)
            {
                var vm = new MachineExtraNodeEditorViewModel(extra.Id, ParentOptions);
                vm.LoadFrom(extra);
                ExtraNodes.Add(vm);
            }

            RebuildNodeCatalog();
        }

        public void ApplyToolMount(MachineGeometryPoint mount)
        {
            ToolMountX = mount.X;
            ToolMountY = mount.Y;
            ToolMountZ = mount.Z;
        }

        public void ApplyNodeAttachFromSphere(string nodeId, MachineGeometryPoint attach, MachineGeometryPoint meshOffset)
        {
            if (TryGetBuiltInEditor(nodeId) is MachineNodeEditorViewModel builtIn)
            {
                builtIn.AttachChildX = attach.X;
                builtIn.AttachChildY = attach.Y;
                builtIn.AttachChildZ = attach.Z;
                builtIn.OffsetX = meshOffset.X;
                builtIn.OffsetY = meshOffset.Y;
                builtIn.OffsetZ = meshOffset.Z;
                return;
            }

            if (TryGetExtraEditor(nodeId) is MachineExtraNodeEditorViewModel extra)
            {
                extra.AttachChildX = attach.X;
                extra.AttachChildY = attach.Y;
                extra.AttachChildZ = attach.Z;
                extra.OffsetX = meshOffset.X;
                extra.OffsetY = meshOffset.Y;
                extra.OffsetZ = meshOffset.Z;
            }
        }

        public MachineGeometryPoint GetWcsPreviewOffset() =>
            new() { X = PreviewWcsG54X, Y = PreviewWcsG54Y, Z = PreviewWcsG54Z };

        /// <summary>Задать ноль MCS в точке сцены (мм): выбранная точка станка становится WORLD/MCS (0,0,0).</summary>
        public void PlaceMcsOriginAtScenePoint(double sceneX, double sceneY, double sceneZ)
        {
            ApplyUiEditorsToDraft();
            _draft.AlignSceneGeometrySoWorldPointAtOrigin(sceneX, sceneY, sceneZ);
            LoadNodesAndMountsFromDraft();
            SyncPreviewSliderLimits();
            NotifyAxisPreviewAndSliderBindings();
            RefreshNodePositionMcsDisplay();
            StatusMessage =
                "MCS перенесён: выбранная точка станка стала WORLD/MCS (0,0,0), взаимное положение узлов сохранено.";
        }

        /// <summary>Клон текущего черновика (без пересборки из UI).</summary>
        public MachineDefinition GetDraftClone() => _draft.Clone();

        /// <summary>Сместить станок так, чтобы мировая точка оказалась в (0,0,0); MCS в сцене — в начале координат.</summary>
        public void AlignMachineSoScenePointAtOrigin(double sceneX, double sceneY, double sceneZ)
        {
            var draft = BuildDraftFromUi();
            draft.AlignSceneGeometrySoWorldPointAtOrigin(sceneX, sceneY, sceneZ);
            _draft = draft;
            LoadNodesAndMountsFromDraft();
            FinishProfileUiLoad();
            StatusMessage = "Станок выровнен: выбранная точка в начале координат сцены, MCS в (0,0,0).";
        }

        public bool TryCenterToolMountXY(Point3D centerInNodeFrame)
        {
            if (centerInNodeFrame.X is double.NaN or double.PositiveInfinity or double.NegativeInfinity ||
                centerInNodeFrame.Y is double.NaN or double.PositiveInfinity or double.NegativeInfinity)
            {
                return false;
            }

            ApplyToolMount(ToolMountHelper.CenterXY(
                centerInNodeFrame,
                new MachineGeometryPoint { X = ToolMountX, Y = ToolMountY, Z = ToolMountZ }));
            return true;
        }

        public void ApplyWorkpieceMount(MachineGeometryPoint mount)
        {
            WorkpieceMountX = mount.X;
            WorkpieceMountY = mount.Y;
            WorkpieceMountZ = mount.Z;
        }

        private void LoadToolMountFromDraft()
        {
            ToolMountNodeId = _draft.ToolMountNodeId;
            ToolMountX = _draft.ToolMount.X;
            ToolMountY = _draft.ToolMount.Y;
            ToolMountZ = _draft.ToolMount.Z;
            UpdateToolMountSummary();
        }

        private void LoadWorkpieceMountFromDraft()
        {
            WorkpieceMountX = _draft.WorkpieceMount.X;
            WorkpieceMountY = _draft.WorkpieceMount.Y;
            WorkpieceMountZ = _draft.WorkpieceMount.Z;
            FixtureHeightMm = _draft.FixtureHeightMm;
            UpdateWorkpieceMountSummary();
        }

        private void UpdateToolMountSummary()
        {
            string nodeName = PreviewNodeOptions.FirstOrDefault(n => string.Equals(n.Id, ToolMountNodeId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? ToolMountNodeId;
            ToolMountSummary = $"Узел: {nodeName} | TCP (лок. узла, мм): {ToolMountX:F1}, {ToolMountY:F1}, {ToolMountZ:F1}";
        }

        private void UpdateWorkpieceMountSummary()
        {
            bool useDefault = Math.Abs(WorkpieceMountX) < 1e-9 && Math.Abs(WorkpieceMountY) < 1e-9 && Math.Abs(WorkpieceMountZ) < 1e-9;
            string plane = useDefault
                ? "по умолчанию (центр XY, верх STL)"
                : $"Z={WorkpieceMountZ:F1} мм";
            string fixture = FixtureHeightMm > 0
                ? $" | оснастка {FixtureHeightMm:F1} мм"
                : string.Empty;
            WorkpieceMountSummary =
                $"Плоскость установки: {plane} | точка (лок. стол): {WorkpieceMountX:F1}, {WorkpieceMountY:F1}, {WorkpieceMountZ:F1}{fixture}";
        }

        private void LoadAxesFromDraft()
        {
            var x = _draft.Axes.First(a => a.Name.Equals("X", StringComparison.OrdinalIgnoreCase));
            var y = _draft.Axes.First(a => a.Name.Equals("Y", StringComparison.OrdinalIgnoreCase));
            var z = _draft.Axes.First(a => a.Name.Equals("Z", StringComparison.OrdinalIgnoreCase));
            // Axes are stored relative to MCS; show them in physical UI fields by adding McsZeroOffset.
            _axisXMin = x.Min + McsShiftX;
            _axisXMax = x.Max + McsShiftX;
            _axisXHome = x.Home + McsShiftX;
            _axisYMin = y.Min + McsShiftY;
            _axisYMax = y.Max + McsShiftY;
            _axisYHome = y.Home + McsShiftY;
            _axisZMin = z.Min + McsShiftZ;
            _axisZMax = z.Max + McsShiftZ;
            _axisZHome = z.Home + McsShiftZ;
            OnPropertyChanged(nameof(AxisXMin));
            OnPropertyChanged(nameof(AxisXMax));
            OnPropertyChanged(nameof(AxisXHome));
            OnPropertyChanged(nameof(AxisYMin));
            OnPropertyChanged(nameof(AxisYMax));
            OnPropertyChanged(nameof(AxisYHome));
            OnPropertyChanged(nameof(AxisZMin));
            OnPropertyChanged(nameof(AxisZMax));
            OnPropertyChanged(nameof(AxisZHome));
            OnPropertyChanged(nameof(AxisXMinMcs));
            OnPropertyChanged(nameof(AxisXMaxMcs));
            OnPropertyChanged(nameof(AxisYMinMcs));
            OnPropertyChanged(nameof(AxisYMaxMcs));
            OnPropertyChanged(nameof(AxisZMinMcs));
            OnPropertyChanged(nameof(AxisZMaxMcs));
            OnPropertyChanged(nameof(AxisXHomeMcs));
            OnPropertyChanged(nameof(AxisYHomeMcs));
            OnPropertyChanged(nameof(AxisZHomeMcs));
        }

        private void SyncPreviewSliderLimits()
        {
            PreviewSliderXMinMcs = AxisXMinMcs;
            PreviewSliderXMaxMcs = AxisXMaxMcs;
            PreviewSliderYMinMcs = AxisYMinMcs;
            PreviewSliderYMaxMcs = AxisYMaxMcs;
            PreviewSliderZMinMcs = AxisZMinMcs;
            PreviewSliderZMaxMcs = AxisZMaxMcs;

            PreviewXMcs = ClampPreviewToSlider(PreviewXMcs, PreviewSliderXMinMcs, PreviewSliderXMaxMcs);
            PreviewYMcs = ClampPreviewToSlider(PreviewYMcs, PreviewSliderYMinMcs, PreviewSliderYMaxMcs);
            PreviewZMcs = ClampPreviewToSlider(PreviewZMcs, PreviewSliderZMinMcs, PreviewSliderZMaxMcs);
        }

        private static double ClampPreviewToSlider(double value, double min, double max)
        {
            if (min > max)
            {
                (min, max) = (max, min);
            }

            return Math.Clamp(value, min, max);
        }

        private void ApplyAxesToDraft()
        {
            foreach (var axis in _draft.Axes)
            {
                switch (axis.Name.ToUpperInvariant())
                {
                    case "X":
                        axis.Min = AxisXMinMcs;
                        axis.Max = AxisXMaxMcs;
                        axis.Home = AxisXHomeMcs;
                        ApplyAxisDriver(axis, 'X', MachineNodeIds.Table, MachineNodeKind.Table);
                        break;
                    case "Y":
                        axis.Min = AxisYMinMcs;
                        axis.Max = AxisYMaxMcs;
                        axis.Home = AxisYHomeMcs;
                        ApplyAxisDriver(axis, 'Y', MachineNodeIds.Table, MachineNodeKind.Table);
                        break;
                    case "Z":
                        axis.Min = AxisZMinMcs;
                        axis.Max = AxisZMaxMcs;
                        axis.Home = AxisZHomeMcs;
                        ApplyAxisDriver(axis, 'Z', MachineNodeIds.Spindle, MachineNodeKind.Spindle);
                        break;
                }
            }
        }

        private void ApplyAxisDriver(MachineAxisDefinition axis, char letter, string defaultNodeId, MachineNodeKind defaultKind)
        {
            string? owner = FindProgramAxisOwner(letter);
            if (owner == MachineNodeIds.Table)
            {
                axis.DriverNodeId = MachineNodeIds.Table;
                axis.DrivenBy = MachineNodeKind.Table;
            }
            else if (owner == MachineNodeIds.Spindle)
            {
                axis.DriverNodeId = MachineNodeIds.Spindle;
                axis.DrivenBy = MachineNodeKind.Spindle;
            }
            else if (owner != null)
            {
                axis.DriverNodeId = owner;
                axis.DrivenBy = defaultKind;
            }
            else
            {
                axis.DriverNodeId = defaultNodeId;
                axis.DrivenBy = defaultKind;
            }
        }
    }

    public sealed class NodePickerItem
    {
        public NodePickerItem(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string DisplayName { get; }
    }

    public sealed class NodeTreeItem : BaseViewModel
    {
        // 0 = solid, 1 = wireframe (edges), 2 = hidden
        private int _displayMode;

        public NodeTreeItem(string id, string displayName, bool canDelete)
        {
            Id = id;
            DisplayName = displayName;
            CanDelete = canDelete;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public bool CanDelete { get; }

        public int DisplayMode
        {
            get => _displayMode;
            set
            {
                if (SetProperty(ref _displayMode, value))
                {
                    OnPropertyChanged(nameof(DisplayGlyph));
                    OnPropertyChanged(nameof(IsSolid));
                }
            }
        }

        // For existing code paths that expect a boolean
        public bool IsSolid
        {
            get => DisplayMode == 0;
            set => DisplayMode = value ? 0 : 2;
        }

        public string DisplayGlyph => DisplayMode switch
        {
            0 => "👁",
            1 => "▦",
            _ => "🚫"
        };
    }

    public sealed class MotionLinkOption
    {
        public MotionLinkOption(MachineMotionLink value, string title)
        {
            Value = value;
            Title = title;
        }

        public MachineMotionLink Value { get; }
        public string Title { get; }
    }

    public static class MachineSetupChoices
    {
        public static IReadOnlyList<MotionLinkOption> MotionLinks { get; } =
        [
            new(MachineMotionLink.Fixed, "Статичный (только крепление)"),
            new(MachineMotionLink.Table, "Перемещается со столом"),
            new(MachineMotionLink.Spindle, "Перемещается со шпинделем")
        ];
    }
}
