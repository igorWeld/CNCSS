using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CNCSS.Machine.Configuration;
using CNCSS.Machine.Model;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Builds and updates the machine assembly in a Helix viewport.</summary>
    public sealed class MachineVisualCoordinator
    {
        private readonly MachineProfileService _profileService;
        private readonly StlMeshLoader _stlLoader;
        private readonly ModelVisual3D _assemblyRoot = new();
        private readonly ModelVisual3D _gizmoOverlayRoot = new();
        private readonly ModelVisual3D _faceMarkerOverlayRoot = new();
        private readonly ModelVisual3D _toolMountOverlayRoot = new();
        private readonly ModelVisual3D _workpieceMountOverlayRoot = new();
        private readonly ModelVisual3D _attachmentSphereOverlayRoot = new();
        private readonly ModelVisual3D _attachmentAxisGizmoRoot = new();
        private readonly ModelVisual3D _wcsPreviewOverlayRoot = new();
        public const double AttachmentGizmoAxisLengthMm = 55;
        private string? _hoveredAttachmentSphereId;
        private string? _pinnedAttachmentSphereHoverId;
        private Point3D _attachmentGizmoCenterAssembly;
        private readonly Dictionary<string, MachineAttachmentSphereVisual> _attachmentSpheres =
            new(StringComparer.OrdinalIgnoreCase);
        private bool _attachmentSpheresVisible = true;
        private bool _wcsPreviewVisible;
        private MachineGeometryPoint _wcsPreviewOffset = MachineGeometryPoint.Zero;
        private readonly Dictionary<Model3D, string> _geometryToNodeId = new();
        private readonly NodeSlot _base = new(MachineNodeIds.Base);
        private readonly NodeSlot _table = new(MachineNodeIds.Table);
        private readonly NodeSlot _spindle = new(MachineNodeIds.Spindle);
        private readonly Dictionary<string, NodeSlot> _extraSlots = new(StringComparer.OrdinalIgnoreCase);
        private HelixViewport3D? _viewport;
        private bool _isAttached;
        private MachineDefinition? _definitionOverride;
        private MachineDefinition? _previewDefinition;
        private string _activeGizmoNodeId = MachineNodeIds.Table;
        private bool _isNodeLayoutEditMode;
        private ModelVisual3D? _activeOverlayGizmo;
        private ModelVisual3D? _toolMountPickMarker;
        private ModelVisual3D? _workpieceMountPickMarker;
        private double _previewAxisX;
        private double _previewAxisY;
        private double _previewAxisZ;
        private bool _previewPoseEstablished;
        /// <summary>
        /// В конструкторе станка: MCS в трансформах узлов (kinematic + McsZeroOffset), корень сборки без сдвига.
        /// В симуляции: сдвиг всей сборки через <see cref="SetWorldTransform"/>.
        /// </summary>
        private bool _bakeSceneMcsIntoNodeTransforms;

        public MachineVisualCoordinator(MachineProfileService profileService, StlMeshLoader stlLoader, bool subscribeToProfileChanges = true)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _stlLoader = stlLoader ?? throw new ArgumentNullException(nameof(stlLoader));

            _assemblyRoot.Children.Add(_base.Root);
            _assemblyRoot.Children.Add(_table.Root);
            _assemblyRoot.Children.Add(_spindle.Root);
            if (subscribeToProfileChanges)
            {
                _profileService.ActiveProfileChanged += OnActiveProfileChanged;
            }
        }

        public ModelVisual3D AssemblyRoot => _assemblyRoot;

        /// <summary>
        /// true — MCS задаётся сдвигом kinematic узлов; false — сдвигом корня сборки (симуляция).
        /// </summary>
        public void SetBakeSceneMcsIntoNodeTransforms(bool bake) => _bakeSceneMcsIntoNodeTransforms = bake;

        /// <summary>
        /// Applies a global world transform for the entire machine assembly and overlays.
        /// Use this to keep the 3D scene origin aligned to the user-defined MCS zero.
        /// </summary>
        public void SetWorldTransform(Transform3D transform)
        {
            _assemblyRoot.Transform = transform;
            _faceMarkerOverlayRoot.Transform = transform;
            _workpieceMountOverlayRoot.Transform = transform;
            _toolMountOverlayRoot.Transform = transform;
            _gizmoOverlayRoot.Transform = transform;
            _attachmentSphereOverlayRoot.Transform = transform;
            _wcsPreviewOverlayRoot.Transform = transform;
        }

        private void ApplyAssemblySceneTransform(MachineDefinition definition)
        {
            var mcs = definition.McsZeroOffset ?? MachineGeometryPoint.Zero;
            if (_bakeSceneMcsIntoNodeTransforms)
            {
                _assemblyRoot.Transform = Transform3D.Identity;
                Transform3D overlayShift = mcs.IsNearlyZero()
                    ? Transform3D.Identity
                    : new TranslateTransform3D(mcs.X, mcs.Y, mcs.Z);
                _faceMarkerOverlayRoot.Transform = overlayShift;
                _workpieceMountOverlayRoot.Transform = overlayShift;
                _toolMountOverlayRoot.Transform = overlayShift;
                _gizmoOverlayRoot.Transform = overlayShift;
                _attachmentSphereOverlayRoot.Transform = overlayShift;
                _wcsPreviewOverlayRoot.Transform = overlayShift;
                return;
            }

            SetWorldTransform(mcs.IsNearlyZero()
                ? Transform3D.Identity
                : new TranslateTransform3D(mcs.X, mcs.Y, mcs.Z));
        }

        private static Transform3D CombineKinematicWithSceneMcs(Transform3D kinematic, MachineGeometryPoint mcs)
        {
            if (mcs.IsNearlyZero())
            {
                return kinematic;
            }

            var group = new Transform3DGroup();
            group.Children.Add(kinematic);
            group.Children.Add(new TranslateTransform3D(mcs.X, mcs.Y, mcs.Z));
            return group;
        }

        /// <summary>
        /// После переноса MCS или смены mesh offset: синхронизировать сдвиг сборки в сцене и локальные трансформы STL.
        /// </summary>
        public void SyncSceneAfterDefinitionKinematicsChange()
        {
            MachineDefinition def = GetDefinition();
            ApplyAssemblySceneTransform(def);
            SyncMeshTransformsFromDefinition();
        }

        public bool TryGetNodeMeshGeometry(string nodeId, out MeshGeometry3D? mesh)
        {
            mesh = null;
            Model3D? content = GetSlot(nodeId).MeshContent;
            if (content is GeometryModel3D geom && geom.Geometry is MeshGeometry3D geometry)
            {
                mesh = geometry;
                return true;
            }

            if (content is Model3DGroup group)
            {
                foreach (Model3D child in group.Children)
                {
                    if (child is GeometryModel3D g && g.Geometry is MeshGeometry3D mg)
                    {
                        mesh = mg;
                        return true;
                    }
                }
            }

            return false;
        }

        public void SetBaseVisible(bool visible) => SetNodeMeshVisible(MachineNodeIds.Base, visible);
        public void SetTableVisible(bool visible) => SetNodeMeshVisible(MachineNodeIds.Table, visible);
        public void SetSpindleVisible(bool visible) => SetNodeMeshVisible(MachineNodeIds.Spindle, visible);

        public void SetNodeVisible(string nodeId, bool visible) => SetNodeMeshVisible(nodeId, visible);

        // 0 = solid, 1 = wireframe (edges), 2 = hidden
        public void SetNodeDisplayMode(string nodeId, int displayMode)
        {
            NodeSlot slot = GetSlot(nodeId);
            slot.DisplayMode = displayMode;
            switch (displayMode)
            {
                case 0:
                    slot.MeshVisual.Content = slot.MeshContent;
                    slot.WireframeVisual.Points = null;
                    break;
                case 1:
                    slot.MeshVisual.Content = null;
                    slot.WireframeVisual.Points = BuildWireframePoints(slot.MeshContent);
                    break;
                default:
                    slot.MeshVisual.Content = null;
                    slot.WireframeVisual.Points = null;
                    break;
            }
        }

        public void SetExtrasVisible(bool visible)
        {
            foreach (var slot in _extraSlots.Values)
            {
                slot.MeshVisual.Content = visible ? slot.MeshContent : null;
            }
        }

        private void SetNodeMeshVisible(string nodeId, bool visible)
        {
            NodeSlot slot = GetSlot(nodeId);
            slot.MeshVisual.Content = visible ? slot.MeshContent : null;
            if (visible)
            {
                slot.WireframeVisual.Points = null;
            }
        }

        private void OnActiveProfileChanged(MachineDefinition definition)
        {
            Dispatcher dispatcher = ResolveUiDispatcher();
            if (dispatcher.CheckAccess())
            {
                _ = RebuildAsync();
            }
            else
            {
                dispatcher.BeginInvoke(RebuildAsync, DispatcherPriority.Background);
            }
        }

        public void Attach(HelixViewport3D viewport)
        {
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            if (!_isAttached)
            {
                viewport.Children.Insert(0, _assemblyRoot);
                viewport.Children.Add(_faceMarkerOverlayRoot);
                viewport.Children.Add(_workpieceMountOverlayRoot);
                viewport.Children.Add(_toolMountOverlayRoot);
                viewport.Children.Add(_gizmoOverlayRoot);
                viewport.Children.Add(_attachmentSphereOverlayRoot);
                viewport.Children.Add(_wcsPreviewOverlayRoot);

                _isAttached = true;
                _previewPoseEstablished = false;
            }

            if (_attachmentSpheresVisible)
            {
                RefreshAttachmentSpheres(_previewAxisX, _previewAxisY, _previewAxisZ);
            }

            _ = RebuildAsync();
        }

        public void Detach()
        {
            if (_viewport != null && _isAttached)
            {
                _viewport.Children.Remove(_assemblyRoot);
                _viewport.Children.Remove(_gizmoOverlayRoot);
                _viewport.Children.Remove(_faceMarkerOverlayRoot);
                _viewport.Children.Remove(_toolMountOverlayRoot);
                _viewport.Children.Remove(_workpieceMountOverlayRoot);
                _viewport.Children.Remove(_attachmentSphereOverlayRoot);
                _viewport.Children.Remove(_wcsPreviewOverlayRoot);

                _isAttached = false;
            }
        }

        public bool IsNodeLayoutEditMode => _isNodeLayoutEditMode;

        public void SetNodeLayoutEditMode(bool enabled)
        {
            _isNodeLayoutEditMode = enabled;
            RefreshGizmoOverlay();
        }

        public void SetPreviewDefinition(MachineDefinition definition) =>
            _previewDefinition = definition ?? throw new ArgumentNullException(nameof(definition));

        public async Task RebuildFromDefinitionAsync(MachineDefinition definition)
        {
            _definitionOverride = definition ?? throw new ArgumentNullException(nameof(definition));
            try
            {
                await RebuildAsync().ConfigureAwait(true);
            }
            finally
            {
                _definitionOverride = null;
            }
        }

        public async Task RebuildPreviewAsync()
        {
            if (_previewDefinition == null)
            {
                return;
            }

            await RebuildAsync().ConfigureAwait(true);
        }

        public async Task RebuildAsync()
        {
            var definition = GetDefinition();
            ApplyAssemblySceneTransform(definition);
            if (!_previewPoseEstablished)
            {
                var physicalHome = definition.GetPhysicalHomePosition();
                _previewAxisX = physicalHome.X;
                _previewAxisY = physicalHome.Y;
                _previewAxisZ = physicalHome.Z;
                _previewPoseEstablished = true;
            }

            SyncExtraSlots(definition);

            await ApplyBuiltInNodeAsync(definition, definition.Base, _base, ResolveNodeColor(definition.Base.MeshColorArgb, MachineNodeColors.ToMediaColor(MachineNodeColors.BasePaleBlue)), BuildPlaceholder(MachineNodeKind.Base)).ConfigureAwait(true);
            await ApplyBuiltInNodeAsync(definition, definition.Table, _table, ResolveNodeColor(definition.Table.MeshColorArgb, MachineNodeColors.ToMediaColor(MachineNodeColors.TablePurple)), BuildPlaceholder(MachineNodeKind.Table)).ConfigureAwait(true);
            await ApplyBuiltInNodeAsync(definition, definition.Spindle, _spindle, ResolveNodeColor(definition.Spindle.MeshColorArgb, MachineNodeColors.ToMediaColor(MachineNodeColors.SpindleGreen)), BuildPlaceholder(MachineNodeKind.Spindle)).ConfigureAwait(true);
            foreach (MachineExtraNodeDefinition extra in definition.ExtraNodes)
            {
                if (_extraSlots.TryGetValue(extra.Id, out NodeSlot? slot))
                {
                    await ApplyExtraNodeAsync(definition, extra, slot).ConfigureAwait(true);
                }
            }

            UpdatePose(_previewAxisX, _previewAxisY, _previewAxisZ);
            ApplyAllDisplayModes();
            RefreshGizmos();
        }

        private void ApplyAllDisplayModes()
        {
            foreach (NodeSlot slot in AllSlots())
            {
                SetNodeDisplayMode(slot.NodeId, slot.DisplayMode);
            }
        }

        public void UpdatePose(double x, double y, double z)
        {
            _previewAxisX = x;
            _previewAxisY = y;
            _previewAxisZ = z;
            _previewPoseEstablished = true;
            var transforms = KinematicChainSolver.SolveTransforms(GetDefinition(), x, y, z);

            ApplyKinematicTransform(_base, transforms);
            ApplyKinematicTransform(_table, transforms);
            ApplyKinematicTransform(_spindle, transforms);

            MachineGeometryPoint mcs = GetDefinition().McsZeroOffset ?? MachineGeometryPoint.Zero;
            foreach (var pair in _extraSlots)
            {
                if (transforms.TryGetValue(pair.Key, out Transform3D? transform))
                {
                    pair.Value.Root.Transform = _bakeSceneMcsIntoNodeTransforms
                        ? CombineKinematicWithSceneMcs(transform, mcs)
                        : transform;
                }
            }

            RefreshGizmoOverlay();
            RefreshToolMountMarker();
            RefreshWorkpieceMountMarker();
            RefreshAttachmentSpheres(x, y, z);
        }

        private void ApplyKinematicTransform(NodeSlot slot, IReadOnlyDictionary<string, Transform3D> transforms)
        {
            if (!transforms.TryGetValue(slot.NodeId, out Transform3D? transform))
            {
                return;
            }

            MachineGeometryPoint mcs = GetDefinition().McsZeroOffset ?? MachineGeometryPoint.Zero;
            slot.Root.Transform = _bakeSceneMcsIntoNodeTransforms
                ? CombineKinematicWithSceneMcs(transform, mcs)
                : transform;
        }

        public (double X, double Y, double Z) GetPreviewPhysicalPose() => (_previewAxisX, _previewAxisY, _previewAxisZ);

        public Point3D GetToolHolderPoint(double x, double y, double z, double stickOutMm = 0) =>
            ToolHolderKinematics.ComputeToolHolderPoint(GetDefinition(), x, y, z, stickOutMm);

        public Point3D GetToolCenterPoint(double x, double y, double z, double stickOutMm = 50) =>
            KinematicChainSolver.ComputeToolCenterPoint(GetDefinition(), x, y, z, stickOutMm);

        public Point3D? TryGetToolMountMeshCenterLocal()
        {
            MachineDefinition def = GetDefinition();
            string nodeId = string.IsNullOrWhiteSpace(def.ToolMountNodeId)
                ? MachineNodeIds.Spindle
                : def.ToolMountNodeId;
            return HasNodeMesh(nodeId) ? GetMeshCenterLocal(nodeId) : null;
        }

        public bool HasNodeMesh(string nodeId) => HasMesh(nodeId);

        private bool HasMesh(string nodeId)
        {
            if (string.Equals(nodeId, MachineNodeIds.Base, StringComparison.OrdinalIgnoreCase))
            {
                return _base.MeshContent != null;
            }

            if (string.Equals(nodeId, MachineNodeIds.Table, StringComparison.OrdinalIgnoreCase))
            {
                return _table.MeshContent != null;
            }

            if (string.Equals(nodeId, MachineNodeIds.Spindle, StringComparison.OrdinalIgnoreCase))
            {
                return _spindle.MeshContent != null;
            }

            return _extraSlots.TryGetValue(nodeId, out NodeSlot? slot) && slot.MeshContent != null;
        }

        public Point3D GetWorkpieceMountPoint(double x, double y, double z)
        {
            var transforms = KinematicChainSolver.SolveTransforms(GetDefinition(), x, y, z);
            Rect3D? bounds = TryGetTableMeshBoundsLocal(out Rect3D tableBounds) && !tableBounds.IsEmpty
                ? tableBounds
                : null;
            return KinematicChainSolver.ComputeWorkpieceMountPoint(GetDefinition(), transforms, bounds);
        }

        public bool TryGetTableMeshBoundsLocal(out Rect3D bounds)
        {
            if (_table.MeshContent == null)
            {
                bounds = Rect3D.Empty;
                return false;
            }

            bounds = StlModelMetrics.ComputeBoundsRecursive(_table.MeshContent);
            return !bounds.IsEmpty;
        }

        public void SetActiveGizmoNode(string nodeId)
        {
            _activeGizmoNodeId = string.IsNullOrWhiteSpace(nodeId) ? MachineNodeIds.Table : nodeId;
            RefreshGizmoOverlay();
        }

        public void RefreshGizmos()
        {
            Dispatcher dispatcher = ResolveUiDispatcher();
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(RefreshGizmos);
                return;
            }

            RebuildMeshIndex();
            CacheGizmoCenters();
            RefreshGizmoOverlay();
        }

        public void SetFaceMarkers(MeshFacePick? reference, MeshFacePick? mobile)
        {
            Dispatcher dispatcher = ResolveUiDispatcher();
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => SetFaceMarkers(reference, mobile));
                return;
            }

            _faceMarkerOverlayRoot.Children.Clear();
            if (reference != null)
            {
                _faceMarkerOverlayRoot.Children.Add(BuildFaceMarker(reference, Colors.Gold));
            }

            if (mobile != null)
            {
                _faceMarkerOverlayRoot.Children.Add(BuildFaceMarker(mobile, Colors.DeepSkyBlue));
            }
        }

        private ModelVisual3D BuildFaceMarker(MeshFacePick pick, Color color)
        {
            Transform3D? meshToWorld = null;
            if (TryGetNodeSlotInfo(pick.NodeId, out NodeSlotInfo slot))
            {
                meshToWorld = slot.MeshToWorld;
            }

            return FaceHighlightBuilder.BuildMarker(pick, color, meshToWorld);
        }

        public void ClearFaceMarkers()
        {
            SetFaceMarkers(null, null);
            SetToolMountFaceMarker(null);
            SetWorkpieceMountFaceMarker(null);
        }

        public void SetToolMountFaceMarker(MeshFacePick? pick)
        {
            Dispatcher dispatcher = ResolveUiDispatcher();
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => SetToolMountFaceMarker(pick));
                return;
            }

            if (_toolMountPickMarker != null)
            {
                _faceMarkerOverlayRoot.Children.Remove(_toolMountPickMarker);
                _toolMountPickMarker = null;
            }

            if (pick != null)
            {
                _toolMountPickMarker = BuildFaceMarker(pick, Colors.Orange);
                _faceMarkerOverlayRoot.Children.Add(_toolMountPickMarker);
            }
        }

        public void RefreshToolMountMarker()
        {
            Dispatcher dispatcher = ResolveUiDispatcher();
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(RefreshToolMountMarker);
                return;
            }

            _toolMountOverlayRoot.Children.Clear();
            try
            {
                MachineDefinition def = GetDefinition();
                Point3D tcp = ToolMountMcsHelper.ComputeTcpPhysical(def, _previewAxisX, _previewAxisY, _previewAxisZ);
                _toolMountOverlayRoot.Children.Add(BuildMarkerSphere(tcp, 6, Color.FromArgb(240, 255, 120, 0)));
            }
            catch
            {
                // ignore until kinematic chain is valid
            }
        }

        public void SetWorkpieceMountFaceMarker(MeshFacePick? pick)
        {
            Dispatcher dispatcher = ResolveUiDispatcher();
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => SetWorkpieceMountFaceMarker(pick));
                return;
            }

            if (_workpieceMountPickMarker != null)
            {
                _faceMarkerOverlayRoot.Children.Remove(_workpieceMountPickMarker);
                _workpieceMountPickMarker = null;
            }

            if (pick != null)
            {
                _workpieceMountPickMarker = BuildFaceMarker(pick, Colors.LimeGreen);
                _faceMarkerOverlayRoot.Children.Add(_workpieceMountPickMarker);
            }
        }

        public void RefreshWorkpieceMountMarker()
        {
            Dispatcher dispatcher = ResolveUiDispatcher();
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(RefreshWorkpieceMountMarker);
                return;
            }

            _workpieceMountOverlayRoot.Children.Clear();
            try
            {
                Point3D mount = GetWorkpieceMountPoint(_previewAxisX, _previewAxisY, _previewAxisZ);
                _workpieceMountOverlayRoot.Children.Add(BuildMarkerSphere(mount, 5, Color.FromArgb(220, 80, 220, 100)));
            }
            catch
            {
                // ignore until kinematic chain is valid
            }
        }

        private static ModelVisual3D BuildTcpSphere(Point3D center, double radiusMm) =>
            BuildMarkerSphere(center, radiusMm, Color.FromArgb(230, 255, 140, 0));

        private static ModelVisual3D BuildMarkerSphere(Point3D center, double radiusMm, Color color)
        {
            var builder = new MeshBuilder(false, false);
            builder.AddSphere(center, radiusMm, 10, 10);
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            var geom = new GeometryModel3D
            {
                Geometry = builder.ToMesh(),
                Material = new EmissiveMaterial(brush),
                BackMaterial = new EmissiveMaterial(brush)
            };
            return new ModelVisual3D { Content = geom };
        }

        public bool TryResolveNodeForModel(Model3D model, out string nodeId, out NodeSlotInfo info) =>
            TryResolveNodeForPick(model, null, null, out nodeId, out info);

        public bool TryResolveNodeForPick(
            Model3D? model,
            Visual3D? visual,
            MeshGeometry3D? mesh,
            out string nodeId,
            out NodeSlotInfo info)
        {
            if (model != null && _geometryToNodeId.TryGetValue(model, out nodeId!))
            {
                info = CreateSlotInfo(GetSlot(nodeId));
                return true;
            }

            if (mesh != null)
            {
                foreach (KeyValuePair<Model3D, string> pair in _geometryToNodeId)
                {
                    if (pair.Key is GeometryModel3D geometry && ReferenceEquals(geometry.Geometry, mesh))
                    {
                        nodeId = pair.Value;
                        info = CreateSlotInfo(GetSlot(nodeId));
                        return true;
                    }
                }
            }

            if (visual != null)
            {
                foreach (NodeSlot slot in AllSlots())
                {
                    if (ReferenceEquals(visual, slot.MeshVisual) || ContainsVisual(slot.MeshVisual, visual))
                    {
                        nodeId = slot.NodeId;
                        info = CreateSlotInfo(slot);
                        return true;
                    }
                }
            }

            nodeId = string.Empty;
            info = null!;
            return false;
        }

        public IReadOnlyList<NodeMeshPickTarget> GetMeshPickTargets()
        {
            var list = new List<NodeMeshPickTarget>();
            foreach (NodeSlot slot in AllSlots())
            {
                // Face picking should only work for visible solid meshes.
                if (slot.MeshContent == null || slot.MeshVisual.Content == null)
                {
                    continue;
                }

                NodeSlotInfo info = CreateSlotInfo(slot);
                CollectPickTargets(slot.MeshContent, slot.NodeId, info, list);
            }

            return list;
        }

        public bool IsNodeHidden(string nodeId) => GetSlot(nodeId).DisplayMode == 2;

        public bool IsOverlayVisual(Visual3D? visual)
        {
            if (visual == null)
            {
                return false;
            }

            return ContainsVisual(_gizmoOverlayRoot, visual)
                   || ContainsVisual(_faceMarkerOverlayRoot, visual)
                   || ContainsVisual(_attachmentSphereOverlayRoot, visual)
                   || ContainsVisual(_attachmentAxisGizmoRoot, visual)
                   || ContainsVisual(_wcsPreviewOverlayRoot, visual);
        }

        public string? HoveredAttachmentSphereId => _hoveredAttachmentSphereId;

        public bool IsAttachmentSphereHoverPinned => !string.IsNullOrWhiteSpace(_pinnedAttachmentSphereHoverId);

        public void PinAttachmentSphereHover(string sphereId)
        {
            _pinnedAttachmentSphereHoverId = sphereId;
            SetAttachmentSphereHovered(sphereId);
        }

        public void ClearPinnedAttachmentSphereHover()
        {
            _pinnedAttachmentSphereHoverId = null;
            SetAttachmentSphereHovered(null);
        }

        public IReadOnlyDictionary<string, Model3D> CollectMeshModelsByNodeId()
        {
            var result = new Dictionary<string, Model3D>(StringComparer.OrdinalIgnoreCase);
            TryAddMesh(MachineNodeIds.Base, _base.MeshContent, result);
            TryAddMesh(MachineNodeIds.Table, _table.MeshContent, result);
            TryAddMesh(MachineNodeIds.Spindle, _spindle.MeshContent, result);
            foreach (KeyValuePair<string, NodeSlot> pair in _extraSlots)
            {
                TryAddMesh(pair.Key, pair.Value.MeshContent, result);
            }

            return result;
        }

        private static void TryAddMesh(string nodeId, Model3D? content, Dictionary<string, Model3D> target)
        {
            if (content != null)
            {
                target[nodeId] = content;
            }
        }

        public bool AttachmentSpheresVisible => _attachmentSpheresVisible;

        public void SetAttachmentSpheresVisible(bool visible)
        {
            _attachmentSpheresVisible = visible;
            if (!visible)
            {
                _attachmentSphereOverlayRoot.Children.Clear();
                _attachmentAxisGizmoRoot.Children.Clear();
                _attachmentSpheres.Clear();
                _hoveredAttachmentSphereId = null;
                return;
            }

            RefreshAttachmentSpheres(_previewAxisX, _previewAxisY, _previewAxisZ);
        }

        public void SetWcsPreview(MachineGeometryPoint wcsOffsetMcs, bool visible)
        {
            _wcsPreviewOffset = wcsOffsetMcs.Clone();
            _wcsPreviewVisible = visible;
            RefreshWcsPreviewOverlay();
        }

        public void RefreshAttachmentSpheres(double physicalX, double physicalY, double physicalZ)
        {
            Dispatcher dispatcher = ResolveUiDispatcher();
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => RefreshAttachmentSpheres(physicalX, physicalY, physicalZ));
                return;
            }

            if (!_attachmentSpheresVisible)
            {
                return;
            }

            MachineDefinition def = GetDefinition();
            double sphereRadius = GetAttachmentSphereRadiusMm(def);
            EnsureAttachmentSphere(MachineAttachmentSphereIds.Mcs, ResolveMcsSphereColor(), sphereRadius);

            foreach (string id in MachineAttachmentSphereIds.SetupNodes)
            {
                Color nodeColor = ResolveAttachmentSphereColor(def, id);
                EnsureAttachmentSphere(id, nodeColor, sphereRadius);
                Point3D pos = MachineKinematics.GetAttachmentPointAssemblyMcs(
                    def, id, physicalX, physicalY, physicalZ);
                _attachmentSpheres[id].SetCenter(pos);
            }

            _attachmentSpheres[MachineAttachmentSphereIds.Mcs].SetCenter(new Point3D(0, 0, 0));

            RefreshAttachmentAxisGizmo();
            RefreshWcsPreviewOverlay();
        }

        public bool TryPickAttachmentSphere(Visual3D? visual, out string sphereId)
        {
            sphereId = string.Empty;
            if (visual == null)
            {
                return false;
            }

            foreach (var pair in _attachmentSpheres)
            {
                if (ContainsVisual(pair.Value, visual))
                {
                    sphereId = pair.Key;
                    return true;
                }
            }

            return false;
        }

        public bool TryPickAttachmentSphere(Model3D? model, out string sphereId)
        {
            sphereId = string.Empty;
            if (model == null)
            {
                return false;
            }

            foreach (var pair in _attachmentSpheres)
            {
                if (ReferenceEquals(pair.Value.PickGeometry, model))
                {
                    sphereId = pair.Key;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Луч по экрану: ближайшее пересечение со сферой привязки (мм).</summary>
        public bool TryPickAttachmentSphereAtScreen(
            HelixViewport3D viewport,
            Point screen,
            double physicalX,
            double physicalY,
            double physicalZ,
            out string sphereId)
        {
            sphereId = string.Empty;
            if (!_attachmentSpheresVisible || _attachmentSpheres.Count == 0)
            {
                return false;
            }

            if (!GizmoAxisDragMath.TryGetCursorRay(viewport, screen, out Ray3D ray))
            {
                return false;
            }

            double bestT = double.PositiveInfinity;
            foreach (var pair in _attachmentSpheres)
            {
                Point3D centerAssembly = GetAttachmentSphereCenterAssembly(
                    pair.Key, physicalX, physicalY, physicalZ);
                Point3D centerWorld = AssemblyPointToWorld(centerAssembly);
                double radius = pair.Value.CurrentRadiusMm;
                if (TryRaySphereIntersection(ray, centerWorld, radius, out double t) && t < bestT)
                {
                    bestT = t;
                    sphereId = pair.Key;
                }
            }

            return !string.IsNullOrEmpty(sphereId);
        }

        private static bool TryRaySphereIntersection(Ray3D ray, Point3D center, double radius, out double t)
        {
            t = 0;
            Vector3D oc = ray.Origin - center;
            Vector3D dir = ray.Direction;
            double b = Vector3D.DotProduct(oc, dir);
            double c = oc.LengthSquared - radius * radius;
            double disc = b * b - c;
            if (disc < 0)
            {
                return false;
            }

            double sqrt = Math.Sqrt(disc);
            double t0 = -b - sqrt;
            double t1 = -b + sqrt;
            if (t0 > 1e-6)
            {
                t = t0;
                return true;
            }

            if (t1 > 1e-6)
            {
                t = t1;
                return true;
            }

            return false;
        }

        public void SetAttachmentSphereHovered(string? sphereId)
        {
            if (!string.IsNullOrWhiteSpace(_pinnedAttachmentSphereHoverId))
            {
                sphereId = _pinnedAttachmentSphereHoverId;
            }

            _hoveredAttachmentSphereId = string.IsNullOrWhiteSpace(sphereId) ? null : sphereId;
            foreach (var pair in _attachmentSpheres)
            {
                bool hovered = string.Equals(pair.Key, sphereId, StringComparison.OrdinalIgnoreCase);
                pair.Value.SetHovered(hovered);
                if (!hovered)
                {
                    pair.Value.RestoreBaseColor();
                }
            }

            RefreshAttachmentAxisGizmo();
        }

        public Point3D GetAttachmentSphereCenterAssembly(string sphereId, double physicalX, double physicalY, double physicalZ)
        {
            if (MachineAttachmentSphereIds.IsMcs(sphereId))
            {
                return new Point3D(0, 0, 0);
            }

            MachineDefinition def = GetDefinition();
            return MachineKinematics.GetAttachmentPointAssemblyMcs(def, sphereId, physicalX, physicalY, physicalZ);
        }

        public Point3D AssemblyPointToWorld(Point3D assemblyPoint)
        {
            var mcs = MachineMcsCoordinates.GetMcsZeroOffset(GetDefinition());
            return new Point3D(
                assemblyPoint.X + mcs.X,
                assemblyPoint.Y + mcs.Y,
                assemblyPoint.Z + mcs.Z);
        }

        public bool TryPickHoveredAttachmentSphereAxis(Point screen, HelixViewport3D viewport, out GizmoAxis axis)
        {
            axis = default;
            if (string.IsNullOrWhiteSpace(_hoveredAttachmentSphereId))
            {
                return false;
            }

            Point3D originWorld = AssemblyPointToWorld(_attachmentGizmoCenterAssembly);
            if (!GizmoAxisDragMath.TryGetCursorRay(viewport, screen, out Ray3D ray))
            {
                return false;
            }

            return GizmoAxisDragMath.TryPickWorldAxis(ray, originWorld, AttachmentGizmoAxisLengthMm, out axis);
        }

        private void RefreshAttachmentAxisGizmo()
        {
            _attachmentAxisGizmoRoot.Children.Clear();
            if (string.IsNullOrWhiteSpace(_hoveredAttachmentSphereId) || !_attachmentSpheresVisible)
            {
                return;
            }

            _attachmentGizmoCenterAssembly = GetAttachmentSphereCenterAssembly(
                _hoveredAttachmentSphereId,
                _previewAxisX,
                _previewAxisY,
                _previewAxisZ);
            AxisGizmoBuildResult gizmo = AxisGizmoBuilder.BuildOverlayWithLength(
                _attachmentGizmoCenterAssembly,
                AttachmentGizmoAxisLengthMm);
            _attachmentAxisGizmoRoot.Children.Add(gizmo.Root);
        }

        private void EnsureAttachmentOverlayRootsAttached()
        {
            if (_attachmentSphereOverlayRoot.Children.Contains(_attachmentAxisGizmoRoot))
            {
                return;
            }

            _attachmentSphereOverlayRoot.Children.Add(_attachmentAxisGizmoRoot);
        }

        private void EnsureAttachmentSphere(string id, Color color, double baseRadiusMm)
        {
            EnsureAttachmentOverlayRootsAttached();

            if (_attachmentSpheres.TryGetValue(id, out MachineAttachmentSphereVisual? existing))
            {
                existing.SetColor(color);
                existing.SetBaseRadius(baseRadiusMm);
                if (!_attachmentSphereOverlayRoot.Children.Contains(existing))
                {
                    _attachmentSphereOverlayRoot.Children.Add(existing);
                }

                return;
            }

            var visual = new MachineAttachmentSphereVisual(id, color, baseRadiusMm);
            _attachmentSpheres[id] = visual;
            _attachmentSphereOverlayRoot.Children.Add(visual);
        }

        private static double GetAttachmentSphereRadiusMm(MachineDefinition definition)
        {
            double extent = Math.Max(
                definition.Base.TargetMaxExtentMm,
                Math.Max(definition.Table.TargetMaxExtentMm, definition.Spindle.TargetMaxExtentMm));
            if (extent < 1)
            {
                extent = 200;
            }

            return Math.Clamp(extent * 0.02, 5, 14);
        }

        private static Color ResolveMcsSphereColor() => Color.FromRgb(255, 215, 0);

        private static Color ResolveAttachmentSphereColor(MachineDefinition definition, string nodeId)
        {
            Color color;
            if (definition.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                color = ResolveNodeColor(
                    builtIn.MeshColorArgb,
                    MachineNodeColors.ToMediaColor(MachineNodeColors.ForKind(builtIn.Kind)));
            }
            else if (definition.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                color = ResolveNodeColor(extra.MeshColorArgb, Colors.MediumPurple);
            }
            else
            {
                color = Colors.LightGray;
            }

            return color.A < 32 ? Color.FromArgb(255, color.R, color.G, color.B) : color;
        }

        private void RefreshWcsPreviewOverlay()
        {
            _wcsPreviewOverlayRoot.Children.Clear();
            if (!_wcsPreviewVisible)
            {
                return;
            }

            Point3D origin = new Point3D(_wcsPreviewOffset.X, _wcsPreviewOffset.Y, _wcsPreviewOffset.Z);
            const double len = 45;
            const double thick = 2.5;
            var axes = new ModelVisual3D
            {
                Transform = new TranslateTransform3D(origin.X, origin.Y, origin.Z)
            };
            axes.Children.Add(new LinesVisual3D
            {
                Color = Colors.Red,
                Thickness = thick,
                Points = new Point3DCollection([new Point3D(0, 0, 0), new Point3D(len, 0, 0)])
            });
            axes.Children.Add(new LinesVisual3D
            {
                Color = Colors.LimeGreen,
                Thickness = thick,
                Points = new Point3DCollection([new Point3D(0, 0, 0), new Point3D(0, len, 0)])
            });
            axes.Children.Add(new LinesVisual3D
            {
                Color = Colors.DeepSkyBlue,
                Thickness = thick,
                Points = new Point3DCollection([new Point3D(0, 0, 0), new Point3D(0, 0, len)])
            });
            _wcsPreviewOverlayRoot.Children.Add(axes);
        }

        private static bool ContainsVisual(Visual3D root, Visual3D target)
        {
            if (ReferenceEquals(root, target))
            {
                return true;
            }

            if (root is not ModelVisual3D container)
            {
                return false;
            }

            foreach (Visual3D child in container.Children)
            {
                if (ContainsVisual(child, target))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectPickTargets(
            Model3D model,
            string nodeId,
            NodeSlotInfo slot,
            List<NodeMeshPickTarget> output)
        {
            if (model is GeometryModel3D geometry && geometry.Geometry is MeshGeometry3D)
            {
                output.Add(new NodeMeshPickTarget
                {
                    NodeId = nodeId,
                    Slot = slot,
                    Geometry = geometry
                });
            }

            if (model is Model3DGroup group)
            {
                foreach (Model3D child in group.Children)
                {
                    CollectPickTargets(child, nodeId, slot, output);
                }
            }
        }

        public bool TryGetNodeSlotInfo(string nodeId, out NodeSlotInfo info)
        {
            try
            {
                info = CreateSlotInfo(GetSlot(nodeId));
                return true;
            }
            catch
            {
                info = null!;
                return false;
            }
        }

        public bool TryPickWireframeNode(Ray3D rayWorld, out string nodeId)
        {
            nodeId = string.Empty;
            double bestDist = 6.0; // mm, selection tolerance
            bool found = false;

            foreach (NodeSlot slot in AllSlots())
            {
                if (slot.DisplayMode != 1 || slot.WireframeVisual.Points == null || slot.WireframeVisual.Points.Count < 2)
                {
                    continue;
                }

                Transform3D meshToWorld = ComposeNodeToWorldTransform(slot);
                Point3DCollection pts = slot.WireframeVisual.Points;
                for (int i = 0; i + 1 < pts.Count; i += 2)
                {
                    Point3D a = meshToWorld.Transform(pts[i]);
                    Point3D b = meshToWorld.Transform(pts[i + 1]);
                    double d = DistanceRayToSegment(rayWorld, a, b);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        nodeId = slot.NodeId;
                        found = true;
                    }
                }
            }

            return found;
        }

        private static double DistanceRayToSegment(Ray3D ray, Point3D segStart, Point3D segEnd)
        {
            Vector3D u = ray.Direction;
            if (u.LengthSquared < 1e-12)
            {
                u = new Vector3D(0, 0, 1);
            }
            else
            {
                u.Normalize();
            }

            Vector3D v = segEnd - segStart;
            Vector3D w = ray.Origin - segStart;
            double a = Vector3D.DotProduct(u, u);
            double b = Vector3D.DotProduct(u, v);
            double c = Vector3D.DotProduct(v, v);
            double d = Vector3D.DotProduct(u, w);
            double e = Vector3D.DotProduct(v, w);
            double denom = a * c - b * b;
            double sc = denom < 1e-12 ? 0 : (b * e - c * d) / denom;
            double tc = (a * e - b * d) / (c < 1e-12 ? a : denom);
            tc = Math.Clamp(tc, 0, 1);

            Point3D pRay = ray.Origin + u * sc;
            Point3D pSeg = segStart + v * tc;
            return (pRay - pSeg).Length;
        }

        public void ApplyMeshLayout(string nodeId, MachineGeometryPoint offset, MachineGeometryPoint rotationDegrees)
        {
            MachineDefinition def = GetDefinition();
            if (def.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                builtIn.MeshOffset = offset.Clone();
                builtIn.MeshRotationDegrees = rotationDegrees.Clone();
                NodeSlot slot = GetSlot(nodeId);
                slot.MeshVisual.Transform = MachineNodeMeshTransforms.BuildMeshLocalTransform(builtIn);
                slot.WireframeVisual.Transform = slot.MeshVisual.Transform;
            }
            else if (def.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                extra.MeshOffset = offset.Clone();
                extra.MeshRotationDegrees = rotationDegrees.Clone();
                NodeSlot slot = GetSlot(nodeId);
                slot.MeshVisual.Transform = MachineNodeMeshTransforms.BuildMeshLocalTransform(extra);
                slot.WireframeVisual.Transform = slot.MeshVisual.Transform;
            }

            UpdatePose(_previewAxisX, _previewAxisY, _previewAxisZ);
            RefreshGizmoOverlay();
        }

        /// <summary>Updates mesh scale/offset/rotation on existing geometry without reloading STL files.</summary>
        public void SyncMeshTransformsFromDefinition()
        {
            MachineDefinition def = GetDefinition();
            ApplyMeshTransform(def.Base, _base);
            ApplyMeshTransform(def.Table, _table);
            ApplyMeshTransform(def.Spindle, _spindle);

            foreach (MachineExtraNodeDefinition extra in def.ExtraNodes)
            {
                if (_extraSlots.TryGetValue(extra.Id, out NodeSlot? slot))
                {
                    ApplyMeshTransform(extra, slot);
                }
            }

            UpdatePose(_previewAxisX, _previewAxisY, _previewAxisZ);
            RefreshGizmoOverlay();
        }

        private static void ApplyMeshTransform(MachineNodeDefinition node, NodeSlot slot)
        {
            slot.MeshVisual.Transform = MachineNodeMeshTransforms.BuildMeshLocalTransform(node);
            slot.WireframeVisual.Transform = slot.MeshVisual.Transform;
            CacheGizmoCenter(slot, node.TargetMaxExtentMm);
        }

        private static void ApplyMeshTransform(MachineExtraNodeDefinition node, NodeSlot slot)
        {
            slot.MeshVisual.Transform = MachineNodeMeshTransforms.BuildMeshLocalTransform(node);
            slot.WireframeVisual.Transform = slot.MeshVisual.Transform;
            CacheGizmoCenter(slot, node.TargetMaxExtentMm);
        }

        public Transform3D GetKinematicTransform(string nodeId) => GetSlot(nodeId).Root.Transform ?? Transform3D.Identity;

        public Transform3D GetMeshTransform(string nodeId) => GetSlot(nodeId).MeshVisual.Transform ?? Transform3D.Identity;

        public double GetGizmoArrowLengthMm(string nodeId) =>
            AxisGizmoBuilder.ComputeArrowLengthMm(TryGetNodeMeshExtentMm(nodeId));

        public double TryGetNodeMeshExtentMm(string nodeId)
        {
            NodeSlot slot = GetSlot(nodeId);
            if (slot.MeshContent != null)
            {
                double extent = StlModelMetrics.GetMaxExtent(slot.MeshContent);
                if (extent > 1e-6)
                {
                    return extent;
                }
            }

            return TryGetTargetExtentMm(nodeId);
        }

        public Point3D GetMeshCenterLocal(string nodeId) => GetSlot(nodeId).GizmoCenterLocal;

        public Point3D GetGizmoCenterWorld(string nodeId)
        {
            NodeSlot slot = GetSlot(nodeId);
            Point3D local = slot.GizmoCenterLocal;
            Transform3D chain = ComposeNodeToWorldTransform(slot);
            return chain.Transform(local);
        }

        public bool TryGetNodeRotation(string nodeId, out double rx, out double ry, out double rz)
        {
            MachineDefinition def = GetDefinition();
            if (def.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                rx = builtIn.MeshRotationDegrees.X;
                ry = builtIn.MeshRotationDegrees.Y;
                rz = builtIn.MeshRotationDegrees.Z;
                return true;
            }

            if (def.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                rx = extra.MeshRotationDegrees.X;
                ry = extra.MeshRotationDegrees.Y;
                rz = extra.MeshRotationDegrees.Z;
                return true;
            }

            rx = ry = rz = 0;
            return false;
        }

        public void SetNodeRotation(string nodeId, double rx, double ry, double rz)
        {
            MachineDefinition def = GetDefinition();
            if (def.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                builtIn.MeshRotationDegrees.X = rx;
                builtIn.MeshRotationDegrees.Y = ry;
                builtIn.MeshRotationDegrees.Z = rz;
                NodeSlot slot = GetSlot(nodeId);
                slot.MeshVisual.Transform = MachineNodeMeshTransforms.BuildMeshLocalTransform(builtIn);
                slot.WireframeVisual.Transform = slot.MeshVisual.Transform;
            }
            else if (def.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                extra.MeshRotationDegrees.X = rx;
                extra.MeshRotationDegrees.Y = ry;
                extra.MeshRotationDegrees.Z = rz;
                NodeSlot slot = GetSlot(nodeId);
                slot.MeshVisual.Transform = MachineNodeMeshTransforms.BuildMeshLocalTransform(extra);
                slot.WireframeVisual.Transform = slot.MeshVisual.Transform;
            }

            UpdatePose(_previewAxisX, _previewAxisY, _previewAxisZ);
            RefreshGizmoOverlay();
        }

        public Transform3D? GetNodeRootTransform(string nodeId) => GetSlot(nodeId).Root.Transform;

        public Transform3D GetNodeToWorldTransform(string nodeId) => ComposeNodeToWorldTransform(GetSlot(nodeId));

        public bool TryGetNodeOffset(string nodeId, out double x, out double y, out double z)
        {
            MachineDefinition def = GetDefinition();
            if (def.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                x = builtIn.MeshOffset.X;
                y = builtIn.MeshOffset.Y;
                z = builtIn.MeshOffset.Z;
                return true;
            }

            if (def.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                x = extra.MeshOffset.X;
                y = extra.MeshOffset.Y;
                z = extra.MeshOffset.Z;
                return true;
            }

            x = y = z = 0;
            return false;
        }

        public void SetNodeOffset(string nodeId, double x, double y, double z)
        {
            MachineDefinition def = GetDefinition();
            if (def.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                builtIn.MeshOffset.X = x;
                builtIn.MeshOffset.Y = y;
                builtIn.MeshOffset.Z = z;
                NodeSlot slot = GetSlot(nodeId);
                slot.MeshVisual.Transform = MachineNodeMeshTransforms.BuildMeshLocalTransform(builtIn);
                slot.WireframeVisual.Transform = slot.MeshVisual.Transform;
            }
            else if (def.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                extra.MeshOffset.X = x;
                extra.MeshOffset.Y = y;
                extra.MeshOffset.Z = z;
                NodeSlot slot = GetSlot(nodeId);
                slot.MeshVisual.Transform = MachineNodeMeshTransforms.BuildMeshLocalTransform(extra);
                slot.WireframeVisual.Transform = slot.MeshVisual.Transform;
            }

            UpdatePose(_previewAxisX, _previewAxisY, _previewAxisZ);
            RefreshGizmoOverlay();
        }

        private void RefreshGizmoOverlay()
        {
            _gizmoOverlayRoot.Children.Clear();
            _activeOverlayGizmo = null;

            if (!_isNodeLayoutEditMode)
            {
                return;
            }

            NodeSlot slot;
            try
            {
                slot = GetSlot(_activeGizmoNodeId);
            }
            catch
            {
                return;
            }

            if (slot.MeshContent == null)
            {
                return;
            }

            double extent = TryGetNodeMeshExtentMm(_activeGizmoNodeId);
            double axisLen = AxisGizmoBuilder.ComputeArrowLengthMm(extent);
            ModelVisual3D gizmoRoot = AxisGizmoBuilder.BuildLineOverlay(slot.GizmoCenterLocal, axisLen);

            var nodeWrapper = new ModelVisual3D
            {
                Transform = ComposeNodeToWorldTransform(slot),
                Content = null
            };
            nodeWrapper.Children.Add(gizmoRoot);
            _gizmoOverlayRoot.Children.Add(nodeWrapper);
            _activeOverlayGizmo = nodeWrapper;
        }

        private void CacheGizmoCenters()
        {
            CacheGizmoCenter(_base, GetDefinition().Base.TargetMaxExtentMm);
            CacheGizmoCenter(_table, GetDefinition().Table.TargetMaxExtentMm);
            CacheGizmoCenter(_spindle, GetDefinition().Spindle.TargetMaxExtentMm);
            foreach (MachineExtraNodeDefinition extra in GetDefinition().ExtraNodes)
            {
                if (_extraSlots.TryGetValue(extra.Id, out NodeSlot? slot))
                {
                    CacheGizmoCenter(slot, extra.TargetMaxExtentMm);
                }
            }
        }

        private static void CacheGizmoCenter(NodeSlot slot, double targetExtentMm)
        {
            if (slot.MeshContent == null)
            {
                return;
            }

            slot.GizmoCenterLocal = StlModelMetrics.GetCenter(slot.MeshContent);
        }

        private void RebuildMeshIndex()
        {
            _geometryToNodeId.Clear();
            foreach (NodeSlot slot in AllSlots())
            {
                if (slot.MeshContent == null)
                {
                    continue;
                }

                RegisterGeometry(slot.MeshContent, slot.NodeId);
            }
        }

        private void RegisterGeometry(Model3D model, string nodeId)
        {
            if (model is GeometryModel3D geometry)
            {
                _geometryToNodeId[geometry] = nodeId;
            }

            if (model is Model3DGroup group)
            {
                foreach (Model3D child in group.Children)
                {
                    RegisterGeometry(child, nodeId);
                }
            }
        }

        private NodeSlotInfo CreateSlotInfo(NodeSlot slot) => new()
        {
            NodeId = slot.NodeId,
            KinematicTransform = slot.Root.Transform ?? Transform3D.Identity,
            MeshTransform = slot.MeshVisual.Transform ?? Transform3D.Identity,
            MeshToWorld = ComposeNodeToWorldTransform(slot)
        };

        private static Transform3D ComposeNodeToWorldTransform(NodeSlot slot)
        {
            Transform3D root = slot.Root.Transform ?? Transform3D.Identity;
            Transform3D mesh = slot.MeshVisual.Transform ?? Transform3D.Identity;
            var group = new Transform3DGroup();
            group.Children.Add(root);
            group.Children.Add(mesh);
            return group;
        }

        private double TryGetTargetExtentMm(string nodeId)
        {
            MachineDefinition def = GetDefinition();
            if (def.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                return builtIn.TargetMaxExtentMm > 0 ? builtIn.TargetMaxExtentMm : 120;
            }

            if (def.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                return extra.TargetMaxExtentMm > 0 ? extra.TargetMaxExtentMm : 120;
            }

            return 120;
        }

        private void SyncExtraSlots(MachineDefinition definition)
        {
            var desired = new HashSet<string>(definition.ExtraNodes.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);

            foreach (string existingId in _extraSlots.Keys.ToList())
            {
                if (!desired.Contains(existingId))
                {
                    _assemblyRoot.Children.Remove(_extraSlots[existingId].Root);
                    _extraSlots.Remove(existingId);
                }
            }

            foreach (MachineExtraNodeDefinition extra in definition.ExtraNodes)
            {
                if (_extraSlots.ContainsKey(extra.Id))
                {
                    continue;
                }

                var slot = new NodeSlot(extra.Id);
                _extraSlots[extra.Id] = slot;
                _assemblyRoot.Children.Add(slot.Root);
            }
        }

        private async Task ApplyBuiltInNodeAsync(
            MachineDefinition definition,
            MachineNodeDefinition node,
            NodeSlot slot,
            Color fallbackColor,
            Model3D placeholder)
        {
            await LoadMeshIntoSlotAsync(definition, node.StlFileName, slot, fallbackColor, () => placeholder).ConfigureAwait(false);
            Dispatcher dispatcher = ResolveUiDispatcher();
            await dispatcher.InvokeAsync(() =>
            {
                slot.MeshVisual.Transform = MachineNodeMeshTransforms.BuildMeshLocalTransform(node);
                slot.WireframeVisual.Transform = slot.MeshVisual.Transform;
                CacheGizmoCenter(slot, node.TargetMaxExtentMm);
                RebuildMeshIndex();
                RefreshGizmoOverlay();
            }, DispatcherPriority.Send);
        }

        private async Task ApplyExtraNodeAsync(MachineDefinition definition, MachineExtraNodeDefinition extra, NodeSlot slot)
        {
            await LoadMeshIntoSlotAsync(definition, extra.StlFileName, slot, ResolveNodeColor(extra.MeshColorArgb, Colors.MediumPurple), BuildExtraPlaceholder).ConfigureAwait(false);
            Dispatcher dispatcher = ResolveUiDispatcher();
            await dispatcher.InvokeAsync(() =>
            {
                slot.MeshVisual.Transform = MachineNodeMeshTransforms.BuildMeshLocalTransform(extra);
                slot.WireframeVisual.Transform = slot.MeshVisual.Transform;
                CacheGizmoCenter(slot, extra.TargetMaxExtentMm);
                RebuildMeshIndex();
                RefreshGizmoOverlay();
            }, DispatcherPriority.Send);
        }

        private async Task LoadMeshIntoSlotAsync(
            MachineDefinition definition,
            string stlFileName,
            NodeSlot slot,
            Color fallbackColor,
            Func<Model3D> placeholderFactory)
        {
            string? path = _profileService.Store.ResolveStlFullPath(definition.ProfileId, stlFileName);
            Model3D? model = path != null ? await _stlLoader.LoadAsync(path).ConfigureAwait(false) : null;
            Model3D content = model ?? placeholderFactory();

            Dispatcher dispatcher = ResolveUiDispatcher();
            await dispatcher.InvokeAsync(() =>
            {
                slot.MeshVisual.Content = content;
                slot.MeshContent = content;
                if (slot.MeshVisual.Content is Model3D visual)
                {
                    ApplyColor(visual, fallbackColor);
                }
            }, DispatcherPriority.Send);
        }

        public MachineDefinition GetDefinition() =>
            _definitionOverride ?? _previewDefinition ?? _profileService.ActiveProfile;

        private NodeSlot GetSlot(string nodeId)
        {
            if (string.Equals(nodeId, MachineNodeIds.Base, StringComparison.OrdinalIgnoreCase))
            {
                return _base;
            }

            if (string.Equals(nodeId, MachineNodeIds.Table, StringComparison.OrdinalIgnoreCase))
            {
                return _table;
            }

            if (string.Equals(nodeId, MachineNodeIds.Spindle, StringComparison.OrdinalIgnoreCase))
            {
                return _spindle;
            }

            if (_extraSlots.TryGetValue(nodeId, out NodeSlot? slot))
            {
                return slot;
            }

            throw new ArgumentOutOfRangeException(nameof(nodeId));
        }

        private IEnumerable<NodeSlot> AllSlots()
        {
            yield return _base;
            yield return _table;
            yield return _spindle;
            foreach (NodeSlot slot in _extraSlots.Values)
            {
                yield return slot;
            }
        }

        private Dispatcher ResolveUiDispatcher() =>
            _viewport?.Dispatcher
            ?? Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;

        private static void ApplyColor(Model3D model, Color color)
        {
            if (model is Model3DGroup group)
            {
                foreach (var child in group.Children)
                {
                    ApplyColor(child, color);
                }

                return;
            }

            if (model is GeometryModel3D geom)
            {
                var mat = MaterialHelper.CreateMaterial(color);
                geom.Material = mat;
                geom.BackMaterial = mat;
            }
        }

        private static Color ResolveNodeColor(uint packedArgb, Color fallback)
        {
            if (packedArgb == 0)
            {
                return fallback;
            }

            return Color.FromArgb(
                (byte)(packedArgb >> 24),
                (byte)(packedArgb >> 16),
                (byte)(packedArgb >> 8),
                (byte)packedArgb);
        }

        private static Model3D BuildPlaceholder(MachineNodeKind kind)
        {
            var builder = new MeshBuilder(false, false);
            switch (kind)
            {
                case MachineNodeKind.Base:
                    builder.AddBox(new Point3D(250, 250, 75), 500, 500, 150);
                    break;
                case MachineNodeKind.Table:
                    builder.AddBox(new Point3D(0, 0, 15), 400, 300, 30);
                    break;
                case MachineNodeKind.Spindle:
                    builder.AddBox(new Point3D(0, 0, -100), 80, 80, 200);
                    break;
            }

            var mesh = builder.ToMesh();
            return new GeometryModel3D
            {
                Geometry = mesh,
                Material = MaterialHelper.CreateMaterial(Colors.Gray),
                BackMaterial = MaterialHelper.CreateMaterial(Colors.Gray)
            };
        }

        private static Model3D BuildExtraPlaceholder()
        {
            var builder = new MeshBuilder(false, false);
            builder.AddBox(new Point3D(0, 0, 25), 80, 80, 50);
            var mesh = builder.ToMesh();
            return new GeometryModel3D
            {
                Geometry = mesh,
                Material = MaterialHelper.CreateMaterial(Colors.MediumPurple),
                BackMaterial = MaterialHelper.CreateMaterial(Colors.MediumPurple)
            };
        }

        private sealed class NodeSlot
        {
            public NodeSlot(string nodeId)
            {
                NodeId = nodeId;
                Root.Children.Add(MeshVisual);
                Root.Children.Add(WireframeVisual);
            }

            public string NodeId { get; }
            public ModelVisual3D Root { get; } = new();
            public ModelVisual3D MeshVisual { get; } = new();
            public LinesVisual3D WireframeVisual { get; } = new()
            {
                Color = Colors.White,
                Thickness = 1.0
            };
            public int DisplayMode { get; set; }
            public Model3D? MeshContent { get; set; }
            public Point3D GizmoCenterLocal { get; set; }
        }

        private static Point3DCollection? BuildWireframePoints(Model3D? model) =>
            MeshWireframeBuilder.FromModel(model);
    }
}
