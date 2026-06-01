using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.IO;
using CNCSS.Machine.Configuration;
using CNCSS.Machine.Model;
using CNCSS.UI.ViewModels;
using CNCSS.Vis;
using HelixToolkit.Wpf;
using Microsoft.Win32;

namespace CNCSS.UI.Dialogs
{
    public partial class MachineSetupWindow : Window
    {
        private readonly StlMeshLoader _stlMeshLoader;
        private readonly MeshImportService _meshImportService = new();
        private CadMeshQualitySettings _stepMeshQuality = CadMeshQualitySettings.Standard;
        private readonly MachineSetupViewModel _viewModel;
        private readonly MachineVisualCoordinator _previewCoordinator;
        private readonly MachineNodeOffsetManipulator _offsetManipulator;
        private readonly MachineSurfaceSnapController _surfaceSnap;
        private readonly MachineToolMountController _toolMount;
        private readonly MachineWorkpieceMountController _workpieceMount;
        private readonly MachineAttachmentSphereController _attachmentSpheres;
        private bool _previewHooksAttached;
        private string _selectedPreviewNodeId = MachineNodeIds.Table;
        private bool _nodePanelDragging;
        private Point _nodePanelDragStart;
        private Thickness _nodePanelDragOriginMargin;
        private bool _attachmentSpherePanelDragging;
        private Point _attachmentSpherePanelDragStart;
        private Thickness _attachmentSpherePanelDragOriginMargin;
        private bool _suppressAttachmentSphereWorldText;
        private AttachmentSpherePanelSession? _attachmentSphereSession;
        private bool _suppressNodePropertyPreviewHooks;
        private bool _suppressToolMountPickZToggle;
        private bool _suppressAlignToolToggle;
        private bool _suppressSymmetricToolToggle;
        private bool _hasPreviewSelection;
        private bool _mcsAlignPending;

        public MachineSetupWindow(MachineProfileService profileService, StlMeshLoader stlLoader)
        {
            InitializeComponent();
            _ = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _stlMeshLoader = stlLoader ?? throw new ArgumentNullException(nameof(stlLoader));
            _viewModel = new MachineSetupViewModel(profileService);
            DataContext = _viewModel;
            _previewCoordinator = new MachineVisualCoordinator(profileService, stlLoader, subscribeToProfileChanges: false);
            _surfaceSnap = new MachineSurfaceSnapController(PreviewViewport, _previewCoordinator);
            _toolMount = new MachineToolMountController(PreviewViewport, _previewCoordinator);
            _workpieceMount = new MachineWorkpieceMountController(PreviewViewport, _previewCoordinator);
            _attachmentSpheres = new MachineAttachmentSphereController(PreviewViewport, _previewCoordinator);
            _attachmentSpheres.SetPreviewPoseProvider(() => (_viewModel.PreviewX, _viewModel.PreviewY, _viewModel.PreviewZ));
            _attachmentSpheres.SetDefinitionProvider(() => _previewCoordinator.GetDefinition());
            _attachmentSpheres.SetAttachAppliedHandler(OnAttachmentSphereApplied);
            _attachmentSpheres.SetMcsOriginAppliedHandler(_ =>
            {
                if (_attachmentSphereSession?.IsMcs == true)
                {
                    return;
                }

                CommitMcsOriginFromPreviewCoordinator();
            });
            _attachmentSpheres.SphereClicked += OnAttachmentSphereClicked;
            _offsetManipulator = new MachineNodeOffsetManipulator(PreviewViewport, _previewCoordinator);
            _offsetManipulator.SetOffsetChangedHandler(OnNodeOffsetDragged);
            _offsetManipulator.SetRotationChangedHandler(OnNodeRotationDragged);
            _offsetManipulator.SetSurfaceSnapModeProvider(() =>
                _surfaceSnap.IsSnapMode
                || _toolMount.IsPickZMode
                || _toolMount.IsDragging
                || _workpieceMount.IsPickMode);
            _surfaceSnap.LayoutApplied += OnNodeLayoutApplied;
            _surfaceSnap.AlignFacesCompleted += OnAlignFacesCompleted;
            _surfaceSnap.SymmetricCenterCompleted += OnSymmetricCenterCompleted;
            _surfaceSnap.StateChanged += OnSurfaceSnapStateChanged;
            _toolMount.SetCurrentMountProvider(() => new MachineGeometryPoint
            {
                X = _viewModel.ToolMountX,
                Y = _viewModel.ToolMountY,
                Z = _viewModel.ToolMountZ
            });
            _toolMount.ToolMountApplied += OnToolMountApplied;
            _workpieceMount.WorkpieceMountApplied += OnWorkpieceMountApplied;
            _meshImportService.StepMeshQuality = _stepMeshQuality;
            UpdateStepQualitySummaryText();
            PreviewViewport.PreviewMouseLeftButtonDown += PreviewViewport_PreviewMouseLeftButtonDown;
            PreviewViewport.PreviewMouseMove += PreviewViewport_PreviewMouseMove;
            PreviewViewport.PreviewMouseLeftButtonUp += PreviewViewport_PreviewMouseLeftButtonUp;
            PreviewKeyDown += MachineSetupWindow_PreviewKeyDown;
            Loaded += OnLoaded;
            Closed += (_, _) =>
            {
                PreviewViewport.PreviewMouseLeftButtonDown -= PreviewViewport_PreviewMouseLeftButtonDown;
                PreviewViewport.PreviewMouseMove -= PreviewViewport_PreviewMouseMove;
                PreviewViewport.PreviewMouseLeftButtonUp -= PreviewViewport_PreviewMouseLeftButtonUp;
                PreviewKeyDown -= MachineSetupWindow_PreviewKeyDown;
                _offsetManipulator.Detach();
            };
        }

        public bool WasSaved { get; private set; }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _previewCoordinator.Attach(PreviewViewport);
            _previewCoordinator.SetBakeSceneMcsIntoNodeTransforms(true);
            HookPreviewUpdates();
            await _viewModel.RefreshMissingStlMetricsAsync(_stlMeshLoader);
            _viewModel.SelectedNodeId = _selectedPreviewNodeId;
            SyncNodeSelection();
            UpdateFloatingEditors();
            // Ensure the node modal is visible after initial selection.
            NodeFloatingPanel.Visibility = Visibility.Visible;
            _hasPreviewSelection = true;
            await RefreshPreviewFromDraft();
            SyncAttachmentAndWcsPreview();
            UpdateNodeAxisGizmoState();
            UpdateToolMountInteractionState();
            _ = Dispatcher.BeginInvoke(
                () => ViewportCameraHelper.SetMachineSetupPreviewView(PreviewViewport),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void ModelZoneGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source || IsDescendantOf(PreviewViewport, source))
            {
                return;
            }

            if (IsInteractiveScrollTarget(source))
            {
                return;
            }

            var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = PreviewViewport,
            };
            PreviewViewport.RaiseEvent(forwarded);
            e.Handled = forwarded.Handled;
        }

        private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject? node)
        {
            while (node != null)
            {
                if (ReferenceEquals(node, ancestor))
                {
                    return true;
                }

                node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node) as DependencyObject;
            }

            return false;
        }

        private static bool IsInteractiveScrollTarget(DependencyObject node)
        {
            while (node != null)
            {
                if (node is Slider or ScrollViewer or TextBox or ComboBox or ListBox or ScrollBar)
                {
                    return true;
                }

                node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node) as DependencyObject;
            }

            return false;
        }

        private void PreviewViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point screen = e.GetPosition(PreviewViewport);

            if (_attachmentSpheres.TryHandleMouseDown(screen, isDoubleClick: false))
            {
                e.Handled = true;
                return;
            }

            if (_workpieceMount.IsPickMode && _workpieceMount.TryHandleClick(screen))
            {
                e.Handled = true;
                return;
            }

            if (_toolMount.IsPickZMode && _toolMount.TryHandleClick(screen))
            {
                e.Handled = true;
                return;
            }

            if (_offsetManipulator.TryBeginAxisDrag(screen))
            {
                e.Handled = true;
                return;
            }

            if (_toolMount.TryBeginDrag(screen))
            {
                e.Handled = true;
                return;
            }

            if (_mcsAlignPending
                && _surfaceSnap.ActiveTool == MachineFacePickTool.AlignFaces
                && TryPlaceMcsOriginFromFacePick(screen))
            {
                e.Handled = true;
                return;
            }

            if (_surfaceSnap.IsSnapMode && _surfaceSnap.TryHandleClick(screen))
            {
                e.Handled = true;
                return;
            }

            if (TryPickPreviewNode(screen, out string nodeId))
            {
                SelectPreviewNode(nodeId);
                e.Handled = true;
            }
        }

        private bool TryPickPreviewNode(Point screenPoint, out string nodeId)
        {
            nodeId = string.Empty;
            if (_surfaceSnap.IsSnapMode || _toolMount.IsPickZMode || _workpieceMount.IsPickMode)
            {
                return false;
            }

            return MeshFacePickHelper.TryPickNode(PreviewViewport, _previewCoordinator, screenPoint, out nodeId);
        }

        private void SelectPreviewNode(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            _hasPreviewSelection = true;
            _selectedPreviewNodeId = nodeId;
            _offsetManipulator.SelectedNodeId = nodeId;
            _viewModel.SelectedNodeId = nodeId;
            SyncNodeSelection();
            NodeFloatingPanel.Visibility = Visibility.Visible;
            UpdateNodeTitleEditor();
            UpdateFloatingEditors();
            UpdateToolMountInteractionState();
            UpdateNodeAxisGizmoState();
        }

        private void ClearPreviewSelection()
        {
            if (!_hasPreviewSelection && NodeFloatingPanel.Visibility != Visibility.Visible)
            {
                return;
            }

            _hasPreviewSelection = false;
            _selectedPreviewNodeId = string.Empty;
            _offsetManipulator.SelectedNodeId = MachineNodeIds.Table;
            NodeListBox.SelectedItem = null;
            NodeFloatingPanel.Visibility = Visibility.Collapsed;
            _previewCoordinator.SetNodeLayoutEditMode(false);
            UpdateToolMountInteractionState();
            UpdateNodeAxisGizmoState();
        }

        private void MachineSetupWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            if (_surfaceSnap.ActiveTool == MachineFacePickTool.AlignFaces)
            {
                DeactivateAlignTool();
                UpdateNodeAxisGizmoState();
                e.Handled = true;
                return;
            }

            if (_surfaceSnap.ActiveTool == MachineFacePickTool.SymmetricCenter)
            {
                DeactivateSymmetricTool();
                UpdateNodeAxisGizmoState();
                e.Handled = true;
                return;
            }

            if (_attachmentSphereSession != null)
            {
                CloseAttachmentSpherePanel(applyChanges: false);
                e.Handled = true;
                return;
            }

            ClearPreviewSelection();
            e.Handled = true;
        }

        private void AlignToolToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || sender is not ToggleButton toggle || _suppressAlignToolToggle)
            {
                return;
            }

            if (toggle.IsChecked == true)
            {
                SymmetricToolToggle.IsChecked = false;
                _surfaceSnap.ActiveTool = MachineFacePickTool.AlignFaces;
                SetToolMountPickZMode(false);
                _workpieceMount.IsPickMode = false;
            }
            else if (SymmetricToolToggle.IsChecked != true)
            {
                _surfaceSnap.ActiveTool = MachineFacePickTool.None;
            }

            if (!_surfaceSnap.IsSnapMode)
            {
                _surfaceSnap.ResetPicks();
                _mcsAlignPending = false;
            }

            UpdateNodeAxisGizmoState();
        }

        private void PreviewViewport_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_offsetManipulator.IsDragging)
            {
                return;
            }

            Point screen = e.GetPosition(PreviewViewport);

            if (_attachmentSpheres.TryHandleMouseMove(screen))
            {
                e.Handled = _attachmentSpheres.IsDragging;
                return;
            }

            if (_toolMount.TryUpdateDrag(screen))
            {
                e.Handled = true;
            }
        }

        private void PreviewViewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_attachmentSpheres.IsDragging)
            {
                _attachmentSpheres.EndDrag();
                _ = RefreshPreviewFromDraft();
                e.Handled = true;
                return;
            }

            if (_attachmentSpheres.TryHandleMouseUp())
            {
                e.Handled = true;
                return;
            }

            if (_toolMount.IsDragging)
            {
                _toolMount.EndDrag();
                e.Handled = true;
            }
        }

        private void OnNodeLayoutApplied(string nodeId, MachineGeometryPoint offset, MachineGeometryPoint rotation)
        {
            if (_viewModel.TryGetBuiltInEditor(nodeId) is MachineNodeEditorViewModel builtIn)
            {
                builtIn.OffsetX = offset.X;
                builtIn.OffsetY = offset.Y;
                builtIn.OffsetZ = offset.Z;
                builtIn.MeshRotationX = rotation.X;
                builtIn.MeshRotationY = rotation.Y;
                builtIn.MeshRotationZ = rotation.Z;
            }
            else if (_viewModel.TryGetExtraEditor(nodeId) is MachineExtraNodeEditorViewModel extra)
            {
                extra.OffsetX = offset.X;
                extra.OffsetY = offset.Y;
                extra.OffsetZ = offset.Z;
                extra.MeshRotationX = rotation.X;
                extra.MeshRotationY = rotation.Y;
                extra.MeshRotationZ = rotation.Z;
            }
        }

        private void OnSymmetricCenterCompleted()
        {
            DeactivateSymmetricTool();
            UpdateNodeAxisGizmoState();
        }

        private void DeactivateSymmetricTool()
        {
            if (SymmetricToolToggle != null && SymmetricToolToggle.IsChecked == true)
            {
                _suppressSymmetricToolToggle = true;
                try
                {
                    SymmetricToolToggle.IsChecked = false;
                }
                finally
                {
                    _suppressSymmetricToolToggle = false;
                }
            }

            _surfaceSnap.ActiveTool = MachineFacePickTool.None;
            _surfaceSnap.ResetPicks();
        }

        private void SymmetricToolToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || sender is not ToggleButton toggle || _suppressSymmetricToolToggle)
            {
                return;
            }

            if (toggle.IsChecked == true)
            {
                AlignToolToggle.IsChecked = false;
                _surfaceSnap.ActiveTool = MachineFacePickTool.SymmetricCenter;
                SetToolMountPickZMode(false);
                _workpieceMount.IsPickMode = false;
            }
            else if (AlignToolToggle.IsChecked != true)
            {
                _surfaceSnap.ActiveTool = MachineFacePickTool.None;
            }

            if (!_surfaceSnap.IsSnapMode)
            {
                _surfaceSnap.ResetPicks();
                _mcsAlignPending = false;
            }

            UpdateNodeAxisGizmoState();
        }

        private void OnSurfaceSnapStateChanged()
        {
        }

        private void OnAlignFacesCompleted()
        {
            DeactivateAlignTool();
            UpdateNodeAxisGizmoState();
        }

        private void DeactivateAlignTool()
        {
            if (AlignToolToggle != null && AlignToolToggle.IsChecked == true)
            {
                _suppressAlignToolToggle = true;
                try
                {
                    AlignToolToggle.IsChecked = false;
                }
                finally
                {
                    _suppressAlignToolToggle = false;
                }
            }

            _surfaceSnap.ActiveTool = MachineFacePickTool.None;
            _surfaceSnap.ResetPicks();
            _mcsAlignPending = false;
        }

        private void OnToolMountApplied(string nodeId, MachineGeometryPoint mount)
        {
            _viewModel.ToolMountNodeId = MachineNodeIds.Spindle;
            _toolMount.SetToolMountNodeId(MachineNodeIds.Spindle);
            _viewModel.ApplyToolMount(mount);
            RefreshPreview();
        }

        private void ToolMountCenterXyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            _viewModel.CenterToolMountXY();
            RefreshPreview();
        }

        private void ToolMountPickZButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _suppressToolMountPickZToggle)
            {
                return;
            }

            ActivateToolMountPickZMode();
        }

        private void ToolMountPickZButton_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _suppressToolMountPickZToggle)
            {
                return;
            }

            _toolMount.IsPickZMode = false;
            _previewCoordinator.SetToolMountFaceMarker(null);
        }

        private void ActivateToolMountPickZMode()
        {
            SymmetricToolToggle.IsChecked = false;
            AlignToolToggle.IsChecked = false;
            _surfaceSnap.ActiveTool = MachineFacePickTool.None;
            _workpieceMount.IsPickMode = false;
            _previewCoordinator.SetWorkpieceMountFaceMarker(null);
            _viewModel.SelectedNodeId = MachineNodeIds.Spindle;
            _selectedPreviewNodeId = MachineNodeIds.Spindle;
            SyncNodeSelection();
            _toolMount.IsPickZMode = true;
            _toolMount.ResetStatus();
        }

        private void SetToolMountPickZMode(bool enable)
        {
            _toolMount.IsPickZMode = enable;
            if (ToolMountPickZButton == null)
            {
                if (!enable)
                {
                    _previewCoordinator.SetToolMountFaceMarker(null);
                }

                return;
            }

            if (ToolMountPickZButton.IsChecked == enable)
            {
                if (!enable)
                {
                    _previewCoordinator.SetToolMountFaceMarker(null);
                }

                return;
            }

            _suppressToolMountPickZToggle = true;
            try
            {
                ToolMountPickZButton.IsChecked = enable;
            }
            finally
            {
                _suppressToolMountPickZToggle = false;
            }

            if (!enable)
            {
                _previewCoordinator.SetToolMountFaceMarker(null);
            }

            UpdateNodeAxisGizmoState();
        }

        private void UpdateToolMountInteractionState()
        {
            bool spindle = _viewModel.IsSpindleNodeSelected;
            _toolMount.IsInteractionEnabled = spindle;
            _toolMount.SetToolMountNodeId(MachineNodeIds.Spindle);
            if (!spindle)
            {
                SetToolMountPickZMode(false);
                _toolMount.EndDrag();
            }
        }

        private void WorkpiecePlaneButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            bool enable = !_workpieceMount.IsPickMode;
            if (enable)
            {
                SymmetricToolToggle.IsChecked = false;
                AlignToolToggle.IsChecked = false;
                _surfaceSnap.ActiveTool = MachineFacePickTool.None;
                SetToolMountPickZMode(false);
                _viewModel.SelectedNodeId = MachineNodeIds.Table;
                _selectedPreviewNodeId = MachineNodeIds.Table;
                SyncNodeSelection();
                _workpieceMount.ResetStatus();
            }
            else
            {
                _previewCoordinator.SetWorkpieceMountFaceMarker(null);
            }

            _workpieceMount.IsPickMode = enable;
            UpdateNodeAxisGizmoState();
        }

        private void UpdateNodeAxisGizmoState()
        {
            if (!IsLoaded)
            {
                return;
            }

            bool show = _hasPreviewSelection
                          && !string.IsNullOrWhiteSpace(_selectedPreviewNodeId)
                          && string.IsNullOrWhiteSpace(_previewCoordinator.HoveredAttachmentSphereId)
                          && !_surfaceSnap.IsSnapMode
                          && !_toolMount.IsPickZMode
                          && !_workpieceMount.IsPickMode
                          && !_attachmentSpheres.IsDragging
                          && !_attachmentSpheres.IsAxisDragging
                          && !_previewCoordinator.IsNodeHidden(_selectedPreviewNodeId)
                          && _previewCoordinator.HasNodeMesh(_selectedPreviewNodeId);

            _previewCoordinator.SetNodeLayoutEditMode(show);
            if (show)
            {
                _previewCoordinator.SetActiveGizmoNode(_selectedPreviewNodeId);
                _offsetManipulator.RefreshGizmos();
            }
        }

        private void OnWorkpieceMountApplied(MachineGeometryPoint mount)
        {
            _viewModel.ApplyWorkpieceMount(mount);
            _ = RefreshPreviewFromDraft();
        }

        private void OnNodeRotationDragged(string nodeId, double rx, double ry, double rz)
        {
            if (_viewModel.TryGetBuiltInEditor(nodeId) is MachineNodeEditorViewModel builtIn)
            {
                builtIn.MeshRotationX = rx;
                builtIn.MeshRotationY = ry;
                builtIn.MeshRotationZ = rz;
            }
            else if (_viewModel.TryGetExtraEditor(nodeId) is MachineExtraNodeEditorViewModel extra)
            {
                extra.MeshRotationX = rx;
                extra.MeshRotationY = ry;
                extra.MeshRotationZ = rz;
            }
        }

        private void HookPreviewUpdates()
        {
            if (_previewHooksAttached)
            {
                return;
            }

            _previewHooksAttached = true;
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(MachineSetupViewModel.ProfileContentRevision))
                {
                    _ = RefreshPreviewFromDraft();
                    ApplyNodeVisibilities();
                    return;
                }

                if (args.PropertyName is nameof(MachineSetupViewModel.PreviewX)
                    or nameof(MachineSetupViewModel.PreviewY)
                    or nameof(MachineSetupViewModel.PreviewZ)
                    or nameof(MachineSetupViewModel.ToolMountX)
                    or nameof(MachineSetupViewModel.ToolMountY)
                    or nameof(MachineSetupViewModel.ToolMountZ)
                    or nameof(MachineSetupViewModel.ToolMountNodeId)
                    or nameof(MachineSetupViewModel.WorkpieceMountX)
                    or nameof(MachineSetupViewModel.WorkpieceMountY)
                    or nameof(MachineSetupViewModel.WorkpieceMountZ)
                    or nameof(MachineSetupViewModel.FixtureHeightMm)
                    or nameof(MachineSetupViewModel.PreviewUseWcsCoordinateSystem)
                    or nameof(MachineSetupViewModel.PreviewWcsG54X)
                    or nameof(MachineSetupViewModel.PreviewWcsG54Y)
                    or nameof(MachineSetupViewModel.PreviewWcsG54Z))
                {
                    if (args.PropertyName is nameof(MachineSetupViewModel.ToolMountNodeId))
                    {
                        _toolMount.SetToolMountNodeId(_viewModel.ToolMountNodeId);
                    }

                    if (args.PropertyName is nameof(MachineSetupViewModel.PreviewUseWcsCoordinateSystem)
                        or nameof(MachineSetupViewModel.PreviewWcsG54X)
                        or nameof(MachineSetupViewModel.PreviewWcsG54Y)
                        or nameof(MachineSetupViewModel.PreviewWcsG54Z))
                    {
                        SyncWcsPreview();
                    }

                    RefreshPreview();
                }
            };

            void HookNode(BaseViewModel node)
            {
                node.PropertyChanged += (_, args) =>
                {
                    if (_suppressNodePropertyPreviewHooks)
                    {
                        return;
                    }

                    if (IsNodeMeshTransformProperty(args.PropertyName))
                    {
                        RefreshPreviewMeshTransforms();
                    }
                    else if (args.PropertyName is nameof(MachineNodeEditorViewModel.MeshColor)
                             or nameof(MachineExtraNodeEditorViewModel.MeshColor))
                    {
                        RefreshPreview();
                    }
                    else if (IsNodeAttachProperty(args.PropertyName))
                    {
                        if (TryApplyNodeAttachLayoutPreserving(node, args.PropertyName))
                        {
                            return;
                        }
                    }
                    else if (IsNodeLayoutProperty(args.PropertyName))
                    {
                        _ = RefreshPreviewFromDraft();
                    }
                };
            }

            HookNode(_viewModel.BaseNode);
            HookNode(_viewModel.TableNode);
            HookNode(_viewModel.SpindleNode);
            foreach (MachineExtraNodeEditorViewModel extra in _viewModel.ExtraNodes)
            {
                HookNode(extra);
            }

            _viewModel.ExtraNodes.CollectionChanged += (_, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (MachineExtraNodeEditorViewModel extra in e.NewItems)
                    {
                        HookNode(extra);
                    }
                }

                _viewModel.RebuildNodeCatalog();
                SyncNodeSelection();
                _ = RefreshPreviewFromDraft();
            };
        }

        private void NodePanelClose_Click(object sender, RoutedEventArgs e) =>
            NodeFloatingPanel.Visibility = Visibility.Collapsed;

        private void NodePanelDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 1)
            {
                return;
            }

            _nodePanelDragging = true;
            _nodePanelDragStart = e.GetPosition(ModelZoneGrid);
            _nodePanelDragOriginMargin = NodeFloatingPanel.Margin;
            NodePanelDragHandle.CaptureMouse();
            e.Handled = true;
        }

        private void NodePanelDragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_nodePanelDragging)
            {
                return;
            }

            Point pos = e.GetPosition(ModelZoneGrid);
            double dx = pos.X - _nodePanelDragStart.X;
            double dy = pos.Y - _nodePanelDragStart.Y;
            NodeFloatingPanel.Margin = new Thickness(
                Math.Max(0, _nodePanelDragOriginMargin.Left + dx),
                Math.Max(0, _nodePanelDragOriginMargin.Top + dy),
                0,
                0);
        }

        private void NodePanelDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_nodePanelDragging)
            {
                return;
            }

            _nodePanelDragging = false;
            NodePanelDragHandle.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void NodePanelCollapse_Click(object sender, RoutedEventArgs e)
        {
            if (NodePanelContent.Visibility == Visibility.Visible)
            {
                NodePanelContent.Visibility = Visibility.Collapsed;
                NodePanelCollapseButton.Content = "□";
                NodePanelCollapseButton.ToolTip = "Развернуть";
            }
            else
            {
                NodePanelContent.Visibility = Visibility.Visible;
                NodePanelCollapseButton.Content = "—";
                NodePanelCollapseButton.ToolTip = "Свернуть";
            }
        }

        private void TableMotionX_Changed(object sender, RoutedEventArgs e) =>
            HandleMotionCheckbox(sender, MachineNodeKind.Table, 'X');

        private void TableMotionY_Changed(object sender, RoutedEventArgs e) =>
            HandleMotionCheckbox(sender, MachineNodeKind.Table, 'Y');

        private void TableMotionZ_Changed(object sender, RoutedEventArgs e) =>
            HandleMotionCheckbox(sender, MachineNodeKind.Table, 'Z');

        private void SpindleMotionX_Changed(object sender, RoutedEventArgs e) =>
            HandleMotionCheckbox(sender, MachineNodeKind.Spindle, 'X');

        private void SpindleMotionY_Changed(object sender, RoutedEventArgs e) =>
            HandleMotionCheckbox(sender, MachineNodeKind.Spindle, 'Y');

        private void SpindleMotionZ_Changed(object sender, RoutedEventArgs e) =>
            HandleMotionCheckbox(sender, MachineNodeKind.Spindle, 'Z');

        private void HandleMotionCheckbox(object sender, MachineNodeKind kind, char axis)
        {
            if (!IsLoaded || sender is not CheckBox checkBox)
            {
                return;
            }

            bool enabled = checkBox.IsChecked == true;
            if (!enabled)
            {
                _viewModel.TrySetBuiltInMotion(kind, axis, false, out _);
                return;
            }

            if (!_viewModel.TrySetBuiltInMotion(kind, axis, true, out string error))
            {
                checkBox.IsChecked = false;
                _viewModel.TrySetBuiltInMotion(kind, axis, false, out _);
                MessageBox.Show(this, error, "Оси перемещения", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadModelButton_Click(object sender, RoutedEventArgs e) =>
            _ = ImportModelForNodeAsync(_viewModel.SelectedNodeId);

        private async void ConfirmNodePanel_Click(object sender, RoutedEventArgs e)
        {
            await RefreshPreviewFromDraft();
            if (TrySave())
            {
                WasSaved = true;
            }
        }

        private void NodeDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string nodeId })
            {
                return;
            }

            if (_viewModel.TryGetExtraEditor(nodeId) is not MachineExtraNodeEditorViewModel extra)
            {
                return;
            }

            if (string.Equals(_selectedPreviewNodeId, extra.Id, StringComparison.OrdinalIgnoreCase))
            {
                _selectedPreviewNodeId = MachineNodeIds.Table;
                _viewModel.SelectedNodeId = MachineNodeIds.Table;
            }

            _viewModel.RemoveExtraNode(extra);
            SyncNodeSelection();
            UpdateFloatingEditors();
            _ = RefreshPreviewFromDraft();
        }

        private void LoadModelForNode(string nodeId) => _ = ImportModelForNodeAsync(nodeId);

        private void NodeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || NodeListBox.SelectedValue is not string nodeId)
            {
                return;
            }

            SelectPreviewNode(nodeId);
        }

        private void NodeDisplayMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: NodeTreeItem item })
            {
                return;
            }

            // 0 = solid, 1 = wireframe, 2 = hidden
            item.DisplayMode = (item.DisplayMode + 1) % 3;
            _previewCoordinator.SetNodeDisplayMode(item.Id, item.DisplayMode);
        }

        private void UpdateNodeTitleEditor()
        {
            bool extra = _viewModel.IsExtraNodeSelected;
            NodeTitleText.Visibility = extra ? Visibility.Collapsed : Visibility.Visible;
            NodeTitleEdit.Visibility = extra ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateFloatingEditors()
        {
            string id = _viewModel.SelectedNodeId;
            if (_viewModel.TryGetExtraEditor(id) is MachineExtraNodeEditorViewModel extra)
            {
                ExtraPositionHost.Content = extra;
            }
            else
            {
                ExtraPositionHost.Content = null;
            }
        }

        private void NodeAttachmentPoint_Click(object sender, RoutedEventArgs e)
        {
            string nodeId = _viewModel.SelectedNodeId;
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            if (_viewModel.TryGetBuiltInEditor(nodeId) == null && _viewModel.TryGetExtraEditor(nodeId) == null)
            {
                return;
            }

            OpenAttachmentSpherePanel(nodeId);
        }

        private void AttachmentSpheresToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            SyncAttachmentAndWcsPreview();
            UpdateNodeAxisGizmoState();
        }

        private bool TryPlaceMcsOriginFromFacePick(Point screen)
        {
            if (!MeshFacePickHelper.TryPickMeshFace(PreviewViewport, _previewCoordinator, screen, out MeshFacePick pick))
            {
                _viewModel.StatusMessage = "Грань не найдена. Кликните по модели станка.";
                return true;
            }

            _viewModel.PlaceMcsOriginAtScenePoint(pick.PointWorld.X, pick.PointWorld.Y, pick.PointWorld.Z);
            _previewCoordinator.SetPreviewDefinition(_viewModel.GetDraftClone());
            SyncPreviewSceneAfterMcsChange(_viewModel.PreviewX, _viewModel.PreviewY, _viewModel.PreviewZ);
            _mcsAlignPending = false;
            _surfaceSnap.ResetPicks();
            return true;
        }

        private void OnAttachmentSphereApplied(string nodeId, MachineGeometryPoint attach, MachineGeometryPoint meshOffset)
        {
            _viewModel.ApplyNodeAttachFromSphere(nodeId, attach, meshOffset);
            _viewModel.StatusMessage = $"Точка привязки узла «{nodeId}» обновлена.";
        }

        private void OnAttachmentSphereClicked(string sphereId) => OpenAttachmentSpherePanel(sphereId);

        private void OpenAttachmentSpherePanel(string sphereId)
        {
            bool isMcs = MachineAttachmentSphereIds.IsMcs(sphereId);
            if (!isMcs && _viewModel.TryGetBuiltInEditor(sphereId) == null
                && _viewModel.TryGetExtraEditor(sphereId) == null)
            {
                return;
            }

            string title = _viewModel.PreviewNodeOptions
                .FirstOrDefault(n => string.Equals(n.Id, sphereId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? (isMcs ? "MCS" : sphereId);

            double px = _viewModel.PreviewX;
            double py = _viewModel.PreviewY;
            double pz = _viewModel.PreviewZ;

            MachineDefinition snapshot = _viewModel.GetDraftClone();
            _previewCoordinator.SetPreviewDefinition(snapshot);
            SyncPreviewSceneAfterMcsChange(px, py, pz);

            Point3D initialWorld = MachineAttachmentService.GetAttachmentSphereScenePosition(
                _previewCoordinator.GetDefinition(), sphereId, px, py, pz);

            _attachmentSphereSession = new AttachmentSpherePanelSession
            {
                SphereId = sphereId,
                IsMcs = isMcs,
                Snapshot = snapshot
            };

            AttachmentSphereTitleText.Text = isMcs ? $"MCS — {title}" : $"Точка привязки — {title}";
            AttachmentSphereNodePresetPanel.Visibility = isMcs ? Visibility.Collapsed : Visibility.Visible;
            AttachmentSphereMcsAlignPanel.Visibility = isMcs ? Visibility.Visible : Visibility.Collapsed;
            if (isMcs)
            {
                AttachmentSphereMcsNodeCombo.ItemsSource = _viewModel.PreviewNodeOptions.ToList();
                AttachmentSphereMcsNodeCombo.SelectedIndex =
                    AttachmentSphereMcsNodeCombo.Items.Count > 0 ? 0 : -1;
            }

            if (isMcs)
            {
                AttachmentSphereWorldHeader.Text = "WORLD / MCS (0,0,0 — ноль станка)";
                SetAttachmentSphereWorldFields(new Point3D(0, 0, 0), firePreview: false);
                SetAttachmentSphereWorldInputsReadOnly(true);
            }
            else
            {
                AttachmentSphereWorldHeader.Text = "Мировые координаты WORLD (мм)";
                SetAttachmentSphereWorldFields(initialWorld, firePreview: false);
                SetAttachmentSphereWorldInputsReadOnly(false);
            }

            _previewCoordinator.PinAttachmentSphereHover(sphereId);
            AttachmentSphereFloatingPanel.Visibility = Visibility.Visible;
        }

        private void CloseAttachmentSpherePanel(bool applyChanges)
        {
            if (_attachmentSphereSession == null)
            {
                AttachmentSphereFloatingPanel.Visibility = Visibility.Collapsed;
                _previewCoordinator.ClearPinnedAttachmentSphereHover();
                return;
            }

            AttachmentSpherePanelSession session = _attachmentSphereSession;
            double px = _viewModel.PreviewX;
            double py = _viewModel.PreviewY;
            double pz = _viewModel.PreviewZ;
            _attachmentSphereSession = null;
            AttachmentSphereFloatingPanel.Visibility = Visibility.Collapsed;
            _previewCoordinator.ClearPinnedAttachmentSphereHover();

            if (!applyChanges)
            {
                RestoreAttachmentSphereSessionSnapshot(session, px, py, pz);
                return;
            }

            if (!TryParseAttachmentSphereWorld(out Point3D world))
            {
                RestoreAttachmentSphereSessionSnapshot(session, px, py, pz);
                return;
            }

            if (session.IsMcs)
            {
                CommitMcsOriginFromPreviewCoordinator();
                _previewCoordinator.RefreshAttachmentSpheres(px, py, pz);
                _viewModel.StatusMessage = "Положение MCS обновлено.";
                return;
            }

            ApplyAttachmentSphereWorldTarget(session.SphereId, world);
            RefreshPreviewMeshTransforms();
            _viewModel.StatusMessage = "Точка привязки обновлена.";
        }

        private void AttachmentSpherePanelClose_Click(object sender, RoutedEventArgs e) =>
            CloseAttachmentSpherePanel(applyChanges: false);

        private void AttachmentSpherePanelCancel_Click(object sender, RoutedEventArgs e) =>
            CloseAttachmentSpherePanel(applyChanges: false);

        private void AttachmentSpherePanelConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseAttachmentSphereWorld(out _))
            {
                MessageBox.Show(this, "Введите числовые координаты X, Y, Z.", "Точка привязки",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CloseAttachmentSpherePanel(applyChanges: true);
        }

        private void AttachmentSpherePanelDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 1)
            {
                return;
            }

            _attachmentSpherePanelDragging = true;
            _attachmentSpherePanelDragStart = e.GetPosition(ModelZoneGrid);
            _attachmentSpherePanelDragOriginMargin = AttachmentSphereFloatingPanel.Margin;
            AttachmentSpherePanelDragHandle.CaptureMouse();
            e.Handled = true;
        }

        private void AttachmentSpherePanelDragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_attachmentSpherePanelDragging)
            {
                return;
            }

            Point pos = e.GetPosition(ModelZoneGrid);
            double dx = pos.X - _attachmentSpherePanelDragStart.X;
            double dy = pos.Y - _attachmentSpherePanelDragStart.Y;
            AttachmentSphereFloatingPanel.Margin = new Thickness(
                Math.Max(0, _attachmentSpherePanelDragOriginMargin.Left + dx),
                Math.Max(0, _attachmentSpherePanelDragOriginMargin.Top + dy),
                0,
                0);
        }

        private void AttachmentSpherePanelDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_attachmentSpherePanelDragging)
            {
                return;
            }

            _attachmentSpherePanelDragging = false;
            AttachmentSpherePanelDragHandle.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void SetAttachmentSphereWorldFields(Point3D world, bool firePreview)
        {
            _suppressAttachmentSphereWorldText = true;
            AttachmentSphereWorldXBox.Text = world.X.ToString("F3", CultureInfo.InvariantCulture);
            AttachmentSphereWorldYBox.Text = world.Y.ToString("F3", CultureInfo.InvariantCulture);
            AttachmentSphereWorldZBox.Text = world.Z.ToString("F3", CultureInfo.InvariantCulture);
            _suppressAttachmentSphereWorldText = false;
            if (firePreview)
            {
                PreviewAttachmentSphereWorldFromPanel();
            }
        }

        private void SetAttachmentSphereWorldInputsReadOnly(bool readOnly)
        {
            AttachmentSphereWorldXBox.IsReadOnly = readOnly;
            AttachmentSphereWorldYBox.IsReadOnly = readOnly;
            AttachmentSphereWorldZBox.IsReadOnly = readOnly;
            AttachmentSphereWorldXBox.IsTabStop = !readOnly;
            AttachmentSphereWorldYBox.IsTabStop = !readOnly;
            AttachmentSphereWorldZBox.IsTabStop = !readOnly;
        }

        private void AttachmentSphereWorldCoords_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressAttachmentSphereWorldText || _attachmentSphereSession == null)
            {
                return;
            }

            PreviewAttachmentSphereWorldFromPanel();
        }

        private void PreviewAttachmentSphereWorldFromPanel()
        {
            if (_attachmentSphereSession == null || !TryParseAttachmentSphereWorld(out Point3D world))
            {
                return;
            }

            PreviewAttachmentSphereWorld(
                _attachmentSphereSession.SphereId,
                _attachmentSphereSession.IsMcs,
                world);
        }

        private bool TryParseAttachmentSphereWorld(out Point3D world)
        {
            world = default;
            if (!double.TryParse(AttachmentSphereWorldXBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                || !double.TryParse(AttachmentSphereWorldYBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                || !double.TryParse(AttachmentSphereWorldZBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
            {
                return false;
            }

            world = new Point3D(x, y, z);
            return true;
        }

        private void AttachmentSpherePresetTop_Click(object sender, RoutedEventArgs e) =>
            ApplyAttachmentSphereNodePreset(MeshAttachmentPreset.Top);

        private void AttachmentSpherePresetBottom_Click(object sender, RoutedEventArgs e) =>
            ApplyAttachmentSphereNodePreset(MeshAttachmentPreset.Bottom);

        private void AttachmentSpherePresetCenter_Click(object sender, RoutedEventArgs e) =>
            ApplyAttachmentSphereNodePreset(MeshAttachmentPreset.Center);

        private void ApplyAttachmentSphereNodePreset(MeshAttachmentPreset preset)
        {
            if (_attachmentSphereSession == null)
            {
                return;
            }

            Point3D? scene = TryApplyNodeLocalAttachPreset(_attachmentSphereSession.SphereId, preset);
            if (scene == null)
            {
                MessageBox.Show(this, "Не удалось вычислить точку по STL-модели узла.", "Точка привязки",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetAttachmentSphereWorldFields(scene.Value, firePreview: true);
        }

        private void AttachmentSphereMcsAlignCenter_Click(object sender, RoutedEventArgs e)
        {
            Point3D? center = TryGetMachineBoundsCenterScene();
            if (center == null)
            {
                MessageBox.Show(this, "Нет загруженных STL для вычисления центра станка.", "MCS",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AlignMachineFromAttachmentSpherePanel(center.Value);
        }

        private void AttachmentSphereMcsAlignNodeCenter_Click(object sender, RoutedEventArgs e) =>
            AlignAttachmentSphereMcsToNode(MeshAttachmentPreset.Center);

        private void AttachmentSphereMcsAlignNodeTop_Click(object sender, RoutedEventArgs e) =>
            AlignAttachmentSphereMcsToNode(MeshAttachmentPreset.Top);

        private void AttachmentSphereMcsAlignNodeBottom_Click(object sender, RoutedEventArgs e) =>
            AlignAttachmentSphereMcsToNode(MeshAttachmentPreset.Bottom);

        private void AlignAttachmentSphereMcsToNode(MeshAttachmentPreset preset)
        {
            if (AttachmentSphereMcsNodeCombo.SelectedItem is not NodePickerItem item)
            {
                MessageBox.Show(this, "Выберите узел из списка.", "MCS", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Point3D? scene = TryGetNodeMeshPresetScene(item.Id, preset);
            if (scene == null)
            {
                MessageBox.Show(this, "Не удалось вычислить точку по STL выбранного узла.", "MCS",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AlignMachineFromAttachmentSpherePanel(scene.Value);
        }

        private void AlignMachineFromAttachmentSpherePanel(Point3D sceneTarget)
        {
            if (_attachmentSphereSession == null)
            {
                return;
            }

            ApplyMcsOriginInPreviewCoordinator(sceneTarget);
        }

        private void ApplyMcsOriginInPreviewCoordinator(Point3D sceneTarget)
        {
            MachineDefinition def = _previewCoordinator.GetDefinition();
            double px = _viewModel.PreviewX;
            double py = _viewModel.PreviewY;
            double pz = _viewModel.PreviewZ;
            def.AlignSceneGeometrySoWorldPointAtOrigin(sceneTarget.X, sceneTarget.Y, sceneTarget.Z);
            SyncPreviewSceneAfterMcsChange(px, py, pz);
            SetAttachmentSphereWorldFields(new Point3D(0, 0, 0), firePreview: false);
            SyncWcsPreview();
        }

        private void ApplyDefinitionFromPreviewSuppressingHooks(MachineDefinition definition)
        {
            _suppressNodePropertyPreviewHooks = true;
            try
            {
                _viewModel.ApplyDefinitionFromPreview(definition);
            }
            finally
            {
                _suppressNodePropertyPreviewHooks = false;
            }
        }

        private void CommitMcsOriginFromPreviewCoordinator()
        {
            MachineDefinition applied = _previewCoordinator.GetDefinition().Clone();
            ApplyDefinitionFromPreviewSuppressingHooks(applied);
            SyncPreviewCoordinatorFromViewModelDraft();
            double px = _viewModel.PreviewX;
            double py = _viewModel.PreviewY;
            double pz = _viewModel.PreviewZ;
            _previewCoordinator.RefreshAttachmentSpheres(px, py, pz);
            SetAttachmentSphereWorldFields(new Point3D(0, 0, 0), firePreview: false);
            SyncWcsPreview();
        }

        private void SyncPreviewSceneAfterMcsChange(double px, double py, double pz)
        {
            _previewCoordinator.SyncSceneAfterDefinitionKinematicsChange();
            _previewCoordinator.UpdatePose(px, py, pz);
            _previewCoordinator.RefreshAttachmentSpheres(px, py, pz);
        }

        private void SyncPreviewCoordinatorFromViewModelDraft()
        {
            MachineDefinition draft = _viewModel.GetDraftClone();
            _previewCoordinator.SetPreviewDefinition(draft);
            SyncPreviewSceneAfterMcsChange(_viewModel.PreviewX, _viewModel.PreviewY, _viewModel.PreviewZ);
        }

        private void RestoreAttachmentSphereSessionSnapshot(
            AttachmentSpherePanelSession session,
            double px,
            double py,
            double pz)
        {
            MachineDefinition snapshot = session.Snapshot.Clone();
            ApplyDefinitionFromPreviewSuppressingHooks(snapshot);
            _previewCoordinator.SetPreviewDefinition(snapshot);
            _previewCoordinator.SyncSceneAfterDefinitionKinematicsChange();
            _previewCoordinator.RefreshAttachmentSpheres(px, py, pz);
        }

        private sealed class AttachmentSpherePanelSession
        {
            public required string SphereId { get; init; }
            public required bool IsMcs { get; init; }
            public required MachineDefinition Snapshot { get; init; }
        }

        private void PreviewAttachmentSphereWorld(string sphereId, bool isMcs, Point3D world)
        {
            MachineDefinition def = _previewCoordinator.GetDefinition();
            double px = _viewModel.PreviewX;
            double py = _viewModel.PreviewY;
            double pz = _viewModel.PreviewZ;

            if (isMcs)
            {
                return;
            }

            if (!MachineAttachmentService.TryApplyAttachmentSceneTarget(
                    def,
                    sphereId,
                    world,
                    px,
                    py,
                    pz,
                    out MachineGeometryPoint attach,
                    out MachineGeometryPoint meshOffset))
            {
                return;
            }

            if (def.TryGetBuiltInNode(sphereId) is MachineNodeDefinition builtIn)
            {
                builtIn.AttachOnChild = attach;
                builtIn.MeshOffset = meshOffset;
            }
            else if (def.TryGetExtraNode(sphereId) is MachineExtraNodeDefinition extra)
            {
                extra.AttachOnChild = attach;
                extra.MeshOffset = meshOffset;
            }

            _previewCoordinator.SyncMeshTransformsFromDefinition();
            _previewCoordinator.RefreshAttachmentSpheres(px, py, pz);
        }

        private void ApplyAttachmentSphereWorldTarget(string sphereId, Point3D world)
        {
            MachineDefinition def = _previewCoordinator.GetDefinition();
            double px = _viewModel.PreviewX;
            double py = _viewModel.PreviewY;
            double pz = _viewModel.PreviewZ;

            if (MachineAttachmentSphereIds.IsMcs(sphereId))
            {
                CommitMcsOriginFromPreviewCoordinator();
                return;
            }

            if (!MachineAttachmentService.TryApplyAttachmentSceneTarget(
                    def, sphereId, world, px, py, pz,
                    out MachineGeometryPoint attach,
                    out MachineGeometryPoint meshOffset))
            {
                return;
            }

            if (def.TryGetBuiltInNode(sphereId) != null || def.TryGetExtraNode(sphereId) != null)
            {
                _viewModel.ApplyNodeAttachFromSphere(sphereId, attach, meshOffset);
            }
        }

        private Point3D? TryApplyNodeLocalAttachPreset(string nodeId, MeshAttachmentPreset preset)
        {
            if (!_previewCoordinator.CollectMeshModelsByNodeId().TryGetValue(nodeId, out Model3D? model))
            {
                return null;
            }

            Rect3D bounds = StlModelMetrics.ComputeBoundsRecursive(model);
            if (bounds.IsEmpty)
            {
                return null;
            }

            MachineDefinition def = _previewCoordinator.GetDefinition();
            double px = _viewModel.PreviewX;
            double py = _viewModel.PreviewY;
            double pz = _viewModel.PreviewZ;

            bool ok;
            Point3D attachScene;
            if (def.TryGetBuiltInNode(nodeId) is MachineNodeDefinition builtIn)
            {
                ok = MachineKinematics.TryApplyMeshAttachPreset(
                    builtIn, def, nodeId, preset, bounds, px, py, pz, out attachScene);
            }
            else if (def.TryGetExtraNode(nodeId) is MachineExtraNodeDefinition extra)
            {
                ok = MachineKinematics.TryApplyMeshAttachPreset(
                    extra, def, nodeId, preset, bounds, px, py, pz, out attachScene);
            }
            else
            {
                return null;
            }

            if (!ok)
            {
                return null;
            }

            _previewCoordinator.SyncMeshTransformsFromDefinition();
            _previewCoordinator.RefreshAttachmentSpheres(px, py, pz);
            return attachScene;
        }

        private Point3D? TryGetNodeMeshPresetScene(string nodeId, MeshAttachmentPreset preset)
        {
            if (!_previewCoordinator.CollectMeshModelsByNodeId().TryGetValue(nodeId, out Model3D? mesh))
            {
                return null;
            }

            MachineDefinition def = _previewCoordinator.GetDefinition();
            return MachineAttachmentSceneMath.TryGetMeshPresetScenePoint(
                def,
                nodeId,
                preset,
                mesh,
                _viewModel.PreviewX,
                _viewModel.PreviewY,
                _viewModel.PreviewZ,
                out Point3D scene)
                ? scene
                : null;
        }

        private Point3D? TryGetMachineBoundsCenterScene()
        {
            MachineDefinition def = _previewCoordinator.GetDefinition();
            IReadOnlyDictionary<string, Model3D> meshes = _previewCoordinator.CollectMeshModelsByNodeId();
            if (meshes.Count == 0)
            {
                return null;
            }

            MachineGeometryPoint center = MachineAssemblyMetrics.ComputeSceneBoundsCenter(
                def,
                meshes,
                _viewModel.PreviewX,
                _viewModel.PreviewY,
                _viewModel.PreviewZ);
            return new Point3D(center.X, center.Y, center.Z);
        }

        private void DeleteSelectedNode_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.TryGetExtraEditor(_viewModel.SelectedNodeId) is not MachineExtraNodeEditorViewModel extra)
            {
                MessageBox.Show(this, "Удалить можно только дополнительный узел.", "Узлы", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.Equals(_selectedPreviewNodeId, extra.Id, StringComparison.OrdinalIgnoreCase))
            {
                _selectedPreviewNodeId = MachineNodeIds.Table;
                _viewModel.SelectedNodeId = MachineNodeIds.Table;
            }

            _viewModel.RemoveExtraNode(extra);
            SyncNodeSelection();
            UpdateFloatingEditors();
            _ = RefreshPreviewFromDraft();
        }

        private static bool IsNodeMeshTransformProperty(string? name) =>
            name is nameof(MachineNodeEditorViewModel.TargetMaxExtentMm)
                or nameof(MachineNodeEditorViewModel.MeshScale)
                or nameof(MachineNodeEditorViewModel.OffsetX)
                or nameof(MachineNodeEditorViewModel.OffsetY)
                or nameof(MachineNodeEditorViewModel.OffsetZ)
                or nameof(MachineNodeEditorViewModel.MeshRotationX)
                or nameof(MachineNodeEditorViewModel.MeshRotationY)
                or nameof(MachineNodeEditorViewModel.MeshRotationZ)
                or nameof(MachineExtraNodeEditorViewModel.TargetMaxExtentMm)
                or nameof(MachineExtraNodeEditorViewModel.MeshScale)
                or nameof(MachineExtraNodeEditorViewModel.OffsetX)
                or nameof(MachineExtraNodeEditorViewModel.OffsetY)
                or nameof(MachineExtraNodeEditorViewModel.OffsetZ)
                or nameof(MachineExtraNodeEditorViewModel.MeshRotationX)
                or nameof(MachineExtraNodeEditorViewModel.MeshRotationY)
                or nameof(MachineExtraNodeEditorViewModel.MeshRotationZ);

        private bool TryApplyNodeAttachLayoutPreserving(BaseViewModel editor, string? propertyName)
        {
            if (propertyName == null)
            {
                return false;
            }

            MachineDefinition def = _previewCoordinator.GetDefinition();
            double px = _viewModel.PreviewX;
            double py = _viewModel.PreviewY;
            double pz = _viewModel.PreviewZ;
            bool isChild = IsAttachChildProperty(propertyName);
            bool isParent = IsAttachParentProperty(propertyName);
            if (!isChild && !isParent)
            {
                return false;
            }

            bool ok;
            if (editor is MachineNodeEditorViewModel builtInEditor)
            {
                string nodeId = ResolveBuiltInNodeId(builtInEditor.Kind);
                if (def.TryGetBuiltInNode(nodeId) is not MachineNodeDefinition builtIn)
                {
                    return false;
                }

                if (isChild)
                {
                    ok = MachineKinematics.TrySetAttachOnChildLocalPreservingSubtreeLayout(
                        builtIn,
                        def,
                        nodeId,
                        new MachineGeometryPoint
                        {
                            X = builtInEditor.AttachChildX,
                            Y = builtInEditor.AttachChildY,
                            Z = builtInEditor.AttachChildZ
                        },
                        px,
                        py,
                        pz);
                }
                else
                {
                    ok = MachineKinematics.TrySetAttachOnParentPreservingSubtreeLayout(
                        builtIn,
                        def,
                        nodeId,
                        new MachineGeometryPoint
                        {
                            X = builtInEditor.AttachParentX,
                            Y = builtInEditor.AttachParentY,
                            Z = builtInEditor.AttachParentZ
                        },
                        px,
                        py,
                        pz);
                }

                if (ok)
                {
                    builtInEditor.LoadFrom(builtIn);
                }
            }
            else if (editor is MachineExtraNodeEditorViewModel extraEditor)
            {
                if (def.TryGetExtraNode(extraEditor.Id) is not MachineExtraNodeDefinition extra)
                {
                    return false;
                }

                if (isChild)
                {
                    ok = MachineKinematics.TrySetAttachOnChildLocalPreservingSubtreeLayout(
                        extra,
                        def,
                        extraEditor.Id,
                        new MachineGeometryPoint
                        {
                            X = extraEditor.AttachChildX,
                            Y = extraEditor.AttachChildY,
                            Z = extraEditor.AttachChildZ
                        },
                        px,
                        py,
                        pz);
                }
                else
                {
                    ok = MachineKinematics.TrySetAttachOnParentPreservingSubtreeLayout(
                        extra,
                        def,
                        extraEditor.Id,
                        new MachineGeometryPoint
                        {
                            X = extraEditor.AttachParentX,
                            Y = extraEditor.AttachParentY,
                            Z = extraEditor.AttachParentZ
                        },
                        px,
                        py,
                        pz);
                }

                if (ok)
                {
                    extraEditor.LoadFrom(extra);
                    SyncDescendantExtraEditorsFromDefinition(def, extraEditor.Id);
                }
            }
            else
            {
                return false;
            }

            if (!ok)
            {
                return false;
            }

            RefreshPreviewMeshTransforms();
            _previewCoordinator.UpdatePose(px, py, pz);
            return true;
        }

        private void SyncDescendantExtraEditorsFromDefinition(MachineDefinition def, string rootNodeId)
        {
            foreach (MachineExtraNodeEditorViewModel extraEditor in _viewModel.ExtraNodes)
            {
                if (!IsDescendantOf(def, extraEditor.Id, rootNodeId))
                {
                    continue;
                }

                if (def.TryGetExtraNode(extraEditor.Id) is MachineExtraNodeDefinition extra)
                {
                    extraEditor.LoadFrom(extra);
                }
            }
        }

        private static bool IsDescendantOf(MachineDefinition def, string nodeId, string ancestorId)
        {
            string? currentParent = def.TryGetExtraNode(nodeId)?.ParentNodeId;
            while (!string.IsNullOrEmpty(currentParent))
            {
                if (string.Equals(currentParent, ancestorId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (MachineNodeIds.IsBuiltIn(currentParent))
                {
                    return false;
                }

                currentParent = def.TryGetExtraNode(currentParent)?.ParentNodeId;
            }

            return false;
        }

        private static string ResolveBuiltInNodeId(MachineNodeKind kind) =>
            kind switch
            {
                MachineNodeKind.Base => MachineNodeIds.Base,
                MachineNodeKind.Table => MachineNodeIds.Table,
                MachineNodeKind.Spindle => MachineNodeIds.Spindle,
                _ => MachineNodeIds.Base
            };

        private static bool IsNodeAttachProperty(string? name) =>
            IsAttachChildProperty(name) || IsAttachParentProperty(name);

        private static bool IsAttachChildProperty(string? name) =>
            name is nameof(MachineNodeEditorViewModel.AttachChildX)
                or nameof(MachineNodeEditorViewModel.AttachChildY)
                or nameof(MachineNodeEditorViewModel.AttachChildZ)
                or nameof(MachineExtraNodeEditorViewModel.AttachChildX)
                or nameof(MachineExtraNodeEditorViewModel.AttachChildY)
                or nameof(MachineExtraNodeEditorViewModel.AttachChildZ);

        private static bool IsAttachParentProperty(string? name) =>
            name is nameof(MachineNodeEditorViewModel.AttachParentX)
                or nameof(MachineNodeEditorViewModel.AttachParentY)
                or nameof(MachineNodeEditorViewModel.AttachParentZ)
                or nameof(MachineExtraNodeEditorViewModel.AttachParentX)
                or nameof(MachineExtraNodeEditorViewModel.AttachParentY)
                or nameof(MachineExtraNodeEditorViewModel.AttachParentZ);

        private static bool IsNodeLayoutProperty(string? name) =>
            name is nameof(MachineNodeEditorViewModel.MotionX)
                or nameof(MachineNodeEditorViewModel.MotionY)
                or nameof(MachineNodeEditorViewModel.MotionZ)
                or nameof(MachineExtraNodeEditorViewModel.MotionX)
                or nameof(MachineExtraNodeEditorViewModel.MotionY)
                or nameof(MachineExtraNodeEditorViewModel.MotionZ)
                or nameof(MachineExtraNodeEditorViewModel.MotionLink)
                or nameof(MachineExtraNodeEditorViewModel.ParentNodeId)
                or nameof(MachineExtraNodeEditorViewModel.DisplayName);

        private void RefreshPreview()
        {
            _previewCoordinator.UpdatePose(_viewModel.PreviewX, _viewModel.PreviewY, _viewModel.PreviewZ);
            SyncWcsPreview();
        }

        private void SyncAttachmentAndWcsPreview()
        {
            bool spheresOn = AttachmentSpheresToggle?.IsChecked != false;
            _previewCoordinator.SetAttachmentSpheresVisible(spheresOn);
            _attachmentSpheres.IsEnabled = spheresOn;
            SyncWcsPreview();
        }

        private void SyncWcsPreview() =>
            _previewCoordinator.SetWcsPreview(
                _viewModel.GetWcsPreviewOffset(),
                _viewModel.PreviewUseWcsCoordinateSystem);

        private void RefreshPreviewMeshTransforms()
        {
            var draft = _viewModel.GetDraftClone();
            _previewCoordinator.SetPreviewDefinition(draft);
            _previewCoordinator.SyncMeshTransformsFromDefinition();
            _offsetManipulator.SelectedNodeId = _selectedPreviewNodeId;
            UpdateNodeAxisGizmoState();
        }

        private int _refreshPreviewFromDraftDepth;

        private async Task RefreshPreviewFromDraft()
        {
            if (Interlocked.Increment(ref _refreshPreviewFromDraftDepth) > 1)
            {
                Interlocked.Decrement(ref _refreshPreviewFromDraftDepth);
                return;
            }

            try
            {
                await RefreshPreviewFromDraftCoreAsync().ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Decrement(ref _refreshPreviewFromDraftDepth);
            }
        }

        private async Task RefreshPreviewFromDraftCoreAsync()
        {
            if (_attachmentSphereSession?.IsMcs == true)
            {
                ApplyDefinitionFromPreviewSuppressingHooks(_previewCoordinator.GetDefinition());
            }

            var draft = _viewModel.GetDraftClone();
            _previewCoordinator.SetPreviewDefinition(draft);
            await _previewCoordinator.RebuildPreviewAsync().ConfigureAwait(true);
            _previewCoordinator.UpdatePose(_viewModel.PreviewX, _viewModel.PreviewY, _viewModel.PreviewZ);
            ApplyNodeVisibilities();
            _offsetManipulator.SelectedNodeId = _selectedPreviewNodeId;
            SyncAttachmentAndWcsPreview();
            UpdateNodeAxisGizmoState();
        }

        private void ApplyNodeVisibilities()
        {
            foreach (var item in _viewModel.NodeTree)
            {
                _previewCoordinator.SetNodeDisplayMode(item.Id, item.DisplayMode);
            }
        }

        private void SyncNodeSelection()
        {
            if (_viewModel.NodeTree.Count == 0)
            {
                return;
            }

            bool found = _viewModel.NodeTree.Any(n => string.Equals(n.Id, _selectedPreviewNodeId, StringComparison.OrdinalIgnoreCase));
            if (!found)
            {
                _selectedPreviewNodeId = MachineNodeIds.Table;
                _viewModel.SelectedNodeId = MachineNodeIds.Table;
            }

            NodeListBox.SelectedValue = _selectedPreviewNodeId;
            _offsetManipulator.SelectedNodeId = _selectedPreviewNodeId;
        }

        private void OnNodeOffsetDragged(string nodeId, double x, double y, double z)
        {
            if (_viewModel.TryGetBuiltInEditor(nodeId) is MachineNodeEditorViewModel builtIn)
            {
                builtIn.OffsetX = x;
                builtIn.OffsetY = y;
                builtIn.OffsetZ = z;
                return;
            }

            if (_viewModel.TryGetExtraEditor(nodeId) is MachineExtraNodeEditorViewModel extra)
            {
                extra.OffsetX = x;
                extra.OffsetY = y;
                extra.OffsetZ = z;
            }
        }

        private async void BrowseStl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: MachineNodeKind kind })
            {
                return;
            }

            string nodeId = kind switch
            {
                MachineNodeKind.Base => MachineNodeIds.Base,
                MachineNodeKind.Table => MachineNodeIds.Table,
                MachineNodeKind.Spindle => MachineNodeIds.Spindle,
                _ => MachineNodeIds.Table
            };
            await ImportModelForNodeAsync(nodeId);
        }

        private async void BrowseExtraStl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string nodeId })
            {
                return;
            }

            await ImportModelForNodeAsync(nodeId);
        }

        private void LoadMachineAssembly_Click(object sender, RoutedEventArgs e) =>
            _ = ImportMachineAssemblyAsync();

        private void StepQualitySettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new StepImportQualityDialog(_stepMeshQuality) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            _stepMeshQuality = dialog.SelectedSettings;
            _meshImportService.StepMeshQuality = _stepMeshQuality;
            UpdateStepQualitySummaryText();
        }

        private void UpdateStepQualitySummaryText()
        {
            StepQualitySummaryText.Text = "STEP: " + _stepMeshQuality.DisplayName;
            StepQualitySummaryText.ToolTip = _stepMeshQuality.ShortSummary + Environment.NewLine + _stepMeshQuality.Description;
        }

        private async Task ImportMachineAssemblyAsync()
        {
            string? path = PickModelFile();
            if (path == null)
            {
                return;
            }

            MeshImportResult? result = await TryImportModelFileAsync(path);
            if (result == null)
            {
                return;
            }

            if (result.Components.Count < 2)
            {
                MessageBox.Show(
                    this,
                    "В файле одно тело — это не сборка. Выберите узел в списке и нажмите «Загрузить модель».",
                    "Загрузка сборки",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await ApplyAssemblyImportAsync(result);
        }

        private async Task ImportModelForNodeAsync(string nodeId)
        {
            string? path = PickModelFile();
            if (path == null)
            {
                return;
            }

            MeshImportResult? result = await TryImportModelFileAsync(path);
            if (result == null)
            {
                return;
            }

            if (result.Components.Count >= 2)
            {
                await ApplyAssemblyImportAsync(result);
                return;
            }

            try
            {
                // Allow assigning node color while loading a model.
                if (TryPickNodeColorForImport(out Color? chosen))
                {
                    ApplyNodeColor(nodeId, chosen);
                }
                await AssignModelPathToNodeAsync(nodeId, path);
                await RefreshPreviewFromDraft();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Загрузка модели", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void NodeColorDefault_Click(object sender, RoutedEventArgs e) =>
            ApplyNodeColor(_viewModel.SelectedNodeId, null);

        private void NodeColorSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string tag)
            {
                return;
            }

            var c = (Color)ColorConverter.ConvertFromString(tag);
            ApplyNodeColor(_viewModel.SelectedNodeId, c);
        }

        private void NodeColorMore_Click(object sender, RoutedEventArgs e)
        {
            Color current = GetSelectedNodeColor() ?? Colors.Transparent;
            if (ToolColorPickerInterop.TryPickColor(current, out Color chosen))
            {
                ApplyNodeColor(_viewModel.SelectedNodeId, chosen);
            }
        }

        private Color? GetSelectedNodeColor()
        {
            string nodeId = _viewModel.SelectedNodeId;
            if (_viewModel.TryGetExtraEditor(nodeId) is MachineExtraNodeEditorViewModel extra)
            {
                return extra.MeshColor.A == 0 ? null : extra.MeshColor;
            }

            if (string.Equals(nodeId, MachineNodeIds.Base, StringComparison.OrdinalIgnoreCase)) return _viewModel.BaseNode.MeshColor.A == 0 ? null : _viewModel.BaseNode.MeshColor;
            if (string.Equals(nodeId, MachineNodeIds.Table, StringComparison.OrdinalIgnoreCase)) return _viewModel.TableNode.MeshColor.A == 0 ? null : _viewModel.TableNode.MeshColor;
            if (string.Equals(nodeId, MachineNodeIds.Spindle, StringComparison.OrdinalIgnoreCase)) return _viewModel.SpindleNode.MeshColor.A == 0 ? null : _viewModel.SpindleNode.MeshColor;
            return null;
        }

        private void ApplyNodeColor(string nodeId, Color? color)
        {
            Color c = color ?? Colors.Transparent;
            if (_viewModel.TryGetExtraEditor(nodeId) is MachineExtraNodeEditorViewModel extra)
            {
                extra.MeshColor = c;
                _ = RefreshPreviewFromDraft();
                return;
            }

            if (string.Equals(nodeId, MachineNodeIds.Base, StringComparison.OrdinalIgnoreCase)) _viewModel.BaseNode.MeshColor = c;
            else if (string.Equals(nodeId, MachineNodeIds.Table, StringComparison.OrdinalIgnoreCase)) _viewModel.TableNode.MeshColor = c;
            else if (string.Equals(nodeId, MachineNodeIds.Spindle, StringComparison.OrdinalIgnoreCase)) _viewModel.SpindleNode.MeshColor = c;
            _ = RefreshPreviewFromDraft();
        }

        // Simple modal palette for import flow: OK = apply; Cancel = keep current.
        private bool TryPickNodeColorForImport(out Color? chosen)
        {
            Color? picked = null;
            var swatches = ToolPaletteSwatches.Swatches;
            var wnd = new Window
            {
                Title = "Цвет узла",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White
            };

            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Выберите цвет (Отмена — оставить текущий):", Margin = new Thickness(0, 0, 0, 8) });

            var wrap = new WrapPanel { Width = 260 };
            foreach (var c in swatches)
            {
                var btn = new Button { Width = 30, Height = 30, Margin = new Thickness(4), Background = new SolidColorBrush(c), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
                btn.Click += (_, _) => { picked = c; wnd.DialogResult = true; };
                wrap.Children.Add(btn);
            }
            root.Children.Add(wrap);

            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var def = new Button { Content = "По умолчанию", MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
            def.Click += (_, _) => { picked = null; wnd.DialogResult = true; };
            var cancel = new Button { Content = "Отмена", MinWidth = 80, IsCancel = true };
            bottom.Children.Add(def);
            bottom.Children.Add(cancel);
            root.Children.Add(bottom);

            wnd.Content = root;
            bool ok = wnd.ShowDialog() == true;
            chosen = picked;
            return ok;
        }

        private async Task<MeshImportResult?> TryImportModelFileAsync(string path)
        {
            MeshImportResult result;
            try
            {
                result = await _meshImportService.ImportAsync(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Загрузка модели", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            if (!result.Success)
            {
                MessageBox.Show(
                    this,
                    result.ErrorMessage ?? "Не удалось загрузить модель.",
                    "Загрузка модели",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            if (result.Components.Count > 10
                && !ConfirmLargeAssemblyImport(result.Components.Count))
            {
                return null;
            }

            return result;
        }

        private bool ConfirmLargeAssemblyImport(int componentCount)
        {
            MessageBoxResult answer = MessageBox.Show(
                this,
                $"Обнаружено {componentCount} компонентов. Импорт может занять время и снизить производительность симуляции.\n\nПродолжить?",
                "Большая сборка",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            return answer == MessageBoxResult.Yes;
        }

        private async Task ApplyAssemblyImportAsync(MeshImportResult result)
        {
            var dialog = new ComponentRoleAssignmentDialog(result) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Assignments == null)
            {
                return;
            }

            try
            {
                await _viewModel.ApplyAssemblyImportAsync(dialog.Assignments, _stlMeshLoader);
                _selectedPreviewNodeId = MachineNodeIds.Table;
                _viewModel.SelectedNodeId = MachineNodeIds.Table;
                SyncNodeSelection();
                UpdateFloatingEditors();
                await RefreshPreviewFromDraft();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Загрузка модели", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task AssignModelPathToNodeAsync(string nodeId, string path)
        {
            if (_viewModel.TryGetExtraEditor(nodeId) is MachineExtraNodeEditorViewModel extra)
            {
                await _viewModel.AssignExtraStlAsync(extra.Id, path, _stlMeshLoader);
                return;
            }

            if (string.Equals(nodeId, MachineNodeIds.Base, StringComparison.OrdinalIgnoreCase))
            {
                await _viewModel.AssignStlAsync(MachineNodeKind.Base, path, _stlMeshLoader);
            }
            else if (string.Equals(nodeId, MachineNodeIds.Table, StringComparison.OrdinalIgnoreCase))
            {
                await _viewModel.AssignStlAsync(MachineNodeKind.Table, path, _stlMeshLoader);
            }
            else if (string.Equals(nodeId, MachineNodeIds.Spindle, StringComparison.OrdinalIgnoreCase))
            {
                await _viewModel.AssignStlAsync(MachineNodeKind.Spindle, path, _stlMeshLoader);
            }
            else
            {
                throw new InvalidOperationException("Неизвестный узел: " + nodeId);
            }
        }

        private void ClearStl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: MachineNodeKind kind })
            {
                return;
            }

            _viewModel.ClearStl(kind);
            _ = RefreshPreviewFromDraft();
        }

        private void ClearExtraStl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string nodeId })
            {
                return;
            }

            _viewModel.ClearExtraStl(nodeId);
            _ = RefreshPreviewFromDraft();
        }

        private void AddExtraNode_Click(object sender, RoutedEventArgs e)
        {
            MachineExtraNodeEditorViewModel added = _viewModel.AddExtraNode();
            _selectedPreviewNodeId = added.Id;
            _viewModel.SelectedNodeId = added.Id;
            SyncNodeSelection();
            UpdateFloatingEditors();
            _ = RefreshPreviewFromDraft();
        }

        private static string? PickModelFile()
        {
            var dlg = new OpenFileDialog
            {
                Filter = MeshImportFormats.OpenFileDialogFilter,
                Title = "Выберите 3D модель (STL, OBJ, STEP, IGES)"
            };

            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            string id = Interaction.InputBox("Идентификатор нового профиля:", "Новый профиль", "profile_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            _viewModel.CreateNewProfile(id);
            SyncNodeSelection();
            _ = RefreshPreviewFromDraft();
        }

        private async void LoadMachineProfile_Click(object sender, RoutedEventArgs e)
        {
            string? profileDir = PickMachineProfileDirectory();
            if (profileDir == null)
            {
                return;
            }

            try
            {
                string profileId = _viewModel.ImportProfileFromDirectory(profileDir, makeActive: true);
                SyncNodeSelection();
                await RefreshPreviewFromDraft();
                MessageBox.Show(
                    this,
                    $"Станок загружен в библиотеку как «{profileId}».\n\nПапка библиотеки:\n{_viewModel.ProfilesFolderPath}",
                    "Загрузка станка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Загрузка станка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string? PickMachineProfileDirectory()
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "Выберите папку профиля станка (machine.json и модели STL)",
                InitialDirectory = Directory.Exists(_viewModel.ProfilesFolderPath)
                    ? _viewModel.ProfilesFolderPath
                    : MachineProfileStore.GetDefaultRootDirectory()
            };

            if (folderDialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(folderDialog.FolderName))
            {
                return folderDialog.FolderName;
            }

            var fileDialog = new OpenFileDialog
            {
                Title = "Или выберите файл machine.json",
                Filter = "Профиль станка (machine.json)|machine.json",
                InitialDirectory = folderDialog.InitialDirectory
            };

            if (fileDialog.ShowDialog(this) != true)
            {
                return null;
            }

            return Path.GetDirectoryName(fileDialog.FileName);
        }

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            if (TrySave())
            {
                WasSaved = true;
            }
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = WasSaved;
            Close();
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this, $"Удалить профиль «{_viewModel.SelectedProfileId}»?", "Удаление",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _viewModel.DeleteSelectedProfile();
                SyncNodeSelection();
                _ = RefreshPreviewFromDraft();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Удаление", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool TrySave()
        {
            try
            {
                _viewModel.Save(makeActive: true);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Сохранение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
    }

    internal static class Interaction
    {
        public static string InputBox(string prompt, string title, string defaultValue)
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
            var input = new TextBox { Text = defaultValue, MinWidth = 280 };
            panel.Children.Add(input);

            var wnd = new Window
            {
                Title = title,
                Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current?.MainWindow,
                ResizeMode = ResizeMode.NoResize
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
            string? result = null;
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (_, _) => { result = input.Text; wnd.DialogResult = true; };
            var cancel = new Button { Content = "Отмена", IsCancel = true, MinWidth = 80 };
            cancel.Click += (_, _) => wnd.DialogResult = false;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            wnd.ShowDialog();
            return result ?? string.Empty;
        }
    }
}
