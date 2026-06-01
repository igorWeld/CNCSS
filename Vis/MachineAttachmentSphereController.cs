using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    public sealed class MachineAttachmentSphereController
    {
        private const double ClickDragThresholdPx = 5;

        private readonly HelixViewport3D _viewport;
        private readonly MachineVisualCoordinator _coordinator;
        private bool _enabled = true;
        private bool _isFreeDragging;
        private bool _isAxisDragging;
        private string? _dragSphereId;
        private string? _hoverSphereId;
        private string? _pendingSphereClickId;
        private Point _pendingSphereClickScreen;
        private GizmoAxis _dragAxis;
        private Point3D _dragAxisOriginWorld;
        private Vector3D _dragAxisDirectionWorld;
        private double _dragStartProjection;
        private double _dragLastProjection;
        private Point3D _dragStartCenterAssembly;
        private MachineGeometryPoint _dragStartMcsOffset = MachineGeometryPoint.Zero;
        private MachineGeometryPoint _dragStartAttach = MachineGeometryPoint.Zero;
        private MachineGeometryPoint _dragStartMeshOffset = MachineGeometryPoint.Zero;
        private Func<(double X, double Y, double Z)>? _getPreviewPose;
        private Func<MachineDefinition>? _getDefinition;
        private Action<string, MachineGeometryPoint, MachineGeometryPoint>? _attachApplied;
        private Action<MachineGeometryPoint>? _mcsOriginApplied;

        public MachineAttachmentSphereController(HelixViewport3D viewport, MachineVisualCoordinator coordinator)
        {
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        public bool IsEnabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (!value)
                {
                    EndDrag();
                    SetHovered(null);
                    _pendingSphereClickId = null;
                }
            }
        }

        public bool IsDragging => _isFreeDragging || _isAxisDragging;

        public bool IsAxisDragging => _isAxisDragging;

        public string? SelectedSphereId { get; private set; }

        public string StatusText { get; private set; } = "Клик по сфере — редактор точки привязки; перетащите ось для смещения.";

        public event Action? StateChanged;
        public event Action<string>? SphereClicked;
        public event Action<string, Point3D>? SphereDragCompleted;

        public void SetPreviewPoseProvider(Func<(double X, double Y, double Z)> provider) =>
            _getPreviewPose = provider;

        public void SetDefinitionProvider(Func<MachineDefinition> provider) =>
            _getDefinition = provider;

        public void SetAttachAppliedHandler(Action<string, MachineGeometryPoint, MachineGeometryPoint> handler) =>
            _attachApplied = handler;

        public void SetMcsOriginAppliedHandler(Action<MachineGeometryPoint> handler) =>
            _mcsOriginApplied = handler;

        public bool TryBeginAxisDrag(Point screenPoint)
        {
            if (!_enabled || !_coordinator.AttachmentSpheresVisible || string.IsNullOrWhiteSpace(_hoverSphereId))
            {
                return false;
            }

            if (!_coordinator.TryPickHoveredAttachmentSphereAxis(screenPoint, _viewport, out GizmoAxis axis))
            {
                return false;
            }

            return BeginAxisDrag(_hoverSphereId, axis, screenPoint);
        }

        public bool TryHandleMouseMove(Point screenPoint)
        {
            if (!_enabled || !_coordinator.AttachmentSpheresVisible)
            {
                return false;
            }

            if (_isAxisDragging)
            {
                UpdateAxisDrag(screenPoint);
                return true;
            }

            if (_isFreeDragging)
            {
                UpdateFreeDrag(screenPoint);
                return true;
            }

            if (TryStartPendingAxisDrag(screenPoint))
            {
                return true;
            }

            if (TryPickSphere(screenPoint, out string? hit))
            {
                SetHovered(hit);
                return false;
            }

            SetHovered(null);
            return false;
        }

        public bool TryHandleMouseDown(Point screenPoint, bool isDoubleClick)
        {
            _ = isDoubleClick;
            if (!_enabled || !_coordinator.AttachmentSpheresVisible)
            {
                return false;
            }

            if (!TryPickSphere(screenPoint, out string? hit) || hit == null)
            {
                _pendingSphereClickId = null;
                return false;
            }

            SetHovered(hit);
            SelectedSphereId = hit;
            _pendingSphereClickId = hit;
            _pendingSphereClickScreen = screenPoint;
            StateChanged?.Invoke();
            return true;
        }

        /// <summary>Завершает клик по сфере: открытие редактора, если не было перетаскивания по оси.</summary>
        public bool TryCompletePendingSphereClick()
        {
            if (string.IsNullOrWhiteSpace(_pendingSphereClickId))
            {
                return false;
            }

            string hit = _pendingSphereClickId;
            _pendingSphereClickId = null;
            if (!_isAxisDragging && !_isFreeDragging)
            {
                SphereClicked?.Invoke(hit);
                StateChanged?.Invoke();
            }

            return true;
        }

        public void CancelPendingSphereClick() => _pendingSphereClickId = null;

        public bool TryHandleMouseUp()
        {
            if (_isAxisDragging || _isFreeDragging)
            {
                return false;
            }

            return TryCompletePendingSphereClick();
        }

        public void EndDrag()
        {
            if (_isAxisDragging)
            {
                CommitDraggedSphereToUi();
                _isAxisDragging = false;
            }

            if (_isFreeDragging)
            {
                CommitDraggedSphereToUi();
                _isFreeDragging = false;
            }

            if (_viewport.IsMouseCaptured)
            {
                _viewport.ReleaseMouseCapture();
            }

            _dragSphereId = null;
            _pendingSphereClickId = null;
            StatusText = "Клик по сфере — редактор точки привязки; перетащите ось для смещения.";
            StateChanged?.Invoke();
        }

        private bool TryStartPendingAxisDrag(Point screenPoint)
        {
            if (string.IsNullOrWhiteSpace(_pendingSphereClickId) || _isAxisDragging)
            {
                return false;
            }

            Vector delta = screenPoint - _pendingSphereClickScreen;
            if (delta.Length < ClickDragThresholdPx)
            {
                return false;
            }

            if (!string.Equals(_hoverSphereId, _pendingSphereClickId, StringComparison.OrdinalIgnoreCase)
                || !_coordinator.TryPickHoveredAttachmentSphereAxis(screenPoint, _viewport, out GizmoAxis axis))
            {
                return false;
            }

            _pendingSphereClickId = null;
            if (!BeginAxisDrag(_hoverSphereId!, axis, screenPoint))
            {
                return false;
            }

            _viewport.CaptureMouse();
            return true;
        }

        private bool BeginAxisDrag(string sphereId, GizmoAxis axis, Point screen)
        {
            _dragSphereId = sphereId;
            _dragAxis = axis;
            _dragAxisDirectionWorld = GizmoAxisDragMath.ToUnitAxis(axis);
            _dragAxisOriginWorld = _coordinator.AssemblyPointToWorld(
                _coordinator.GetAttachmentSphereCenterAssembly(
                    sphereId,
                    GetPreviewPose().X,
                    GetPreviewPose().Y,
                    GetPreviewPose().Z));
            _dragStartProjection = GizmoAxisDragMath.ProjectMouseOntoAxis(
                _viewport,
                screen,
                _dragAxisOriginWorld,
                _dragAxisDirectionWorld);
            _dragLastProjection = _dragStartProjection;
            _dragStartCenterAssembly = _coordinator.GetAttachmentSphereCenterAssembly(
                sphereId,
                GetPreviewPose().X,
                GetPreviewPose().Y,
                GetPreviewPose().Z);

            if (_getDefinition?.Invoke() is MachineDefinition def)
            {
                _dragStartMcsOffset = (def.McsZeroOffset ?? MachineGeometryPoint.Zero).Clone();
                if (def.TryGetBuiltInNode(sphereId) is MachineNodeDefinition node)
                {
                    _dragStartAttach = node.AttachOnChild.Clone();
                    _dragStartMeshOffset = node.MeshOffset.Clone();
                }
            }

            _isAxisDragging = true;
            StatusText = MachineAttachmentSphereIds.IsMcs(sphereId)
                ? "Оси MCS: перетаскивание нуля машинных координат."
                : $"Оси точки привязки ({sphereId}): перетаскивание.";
            StateChanged?.Invoke();
            return true;
        }

        private void UpdateAxisDrag(Point screen)
        {
            if (!_isAxisDragging || _dragSphereId == null || _getDefinition == null)
            {
                return;
            }

            double projection = GizmoAxisDragMath.ProjectMouseOntoAxis(
                _viewport,
                screen,
                _dragAxisOriginWorld,
                _dragAxisDirectionWorld);
            double frameDelta = projection - _dragLastProjection;
            _dragLastProjection = projection;
            if (Math.Abs(frameDelta) < 1e-9)
            {
                return;
            }

            if (MachineAttachmentSphereIds.IsMcs(_dragSphereId))
            {
                Vector3D mcsStep = _dragAxisDirectionWorld * frameDelta;
                ApplyMcsSceneDelta(mcsStep);
                return;
            }

            Vector3D step = _dragAxisDirectionWorld * frameDelta;
            ApplyAssemblyDelta(_dragSphereId, step);
        }

        private void ApplyMcsSceneDelta(Vector3D sceneDelta)
        {
            if (_getDefinition?.Invoke() is not MachineDefinition def)
            {
                return;
            }

            (double px, double py, double pz) = GetPreviewPose();
            var mcs = def.McsZeroOffset ?? MachineGeometryPoint.Zero;
            if (!mcs.IsNearlyZero())
            {
                def.MoveMcsOriginPreservingSceneGeometry(MachineGeometryPoint.Zero, px, py, pz);
            }

            def.TranslateMachineGeometryInScene(-sceneDelta.X, -sceneDelta.Y, -sceneDelta.Z);
            _coordinator.SyncSceneAfterDefinitionKinematicsChange();
        }

        private void ApplyAssemblyDelta(string sphereId, Vector3D deltaAssembly)
        {
            if (_getDefinition?.Invoke() is not MachineDefinition def)
            {
                return;
            }

            (double px, double py, double pz) = GetPreviewPose();

            if (MachineAttachmentSphereIds.IsMcs(sphereId))
            {
                return;
            }
            else if (def.TryGetBuiltInNode(sphereId) is MachineNodeDefinition node)
            {
                Point3D current = MachineKinematics.GetAttachmentPointAssemblyMcs(def, sphereId, px, py, pz);
                Point3D target = new(
                    current.X + deltaAssembly.X,
                    current.Y + deltaAssembly.Y,
                    current.Z + deltaAssembly.Z);
                if (MachineKinematics.TrySetAttachmentPointAssemblyMcs(node, def, sphereId, target, px, py, pz))
                {
                    _coordinator.SyncSceneAfterDefinitionKinematicsChange();
                    _attachApplied?.Invoke(sphereId, node.AttachOnChild.Clone(), node.MeshOffset.Clone());
                }
            }
            else if (def.TryGetExtraNode(sphereId) is MachineExtraNodeDefinition extra)
            {
                Point3D current = MachineKinematics.GetAttachmentPointAssemblyMcs(def, sphereId, px, py, pz);
                Point3D target = new(
                    current.X + deltaAssembly.X,
                    current.Y + deltaAssembly.Y,
                    current.Z + deltaAssembly.Z);
                if (MachineKinematics.TrySetAttachmentPointAssemblyMcs(extra, def, sphereId, target, px, py, pz))
                {
                    _coordinator.SyncSceneAfterDefinitionKinematicsChange();
                    _attachApplied?.Invoke(sphereId, extra.AttachOnChild.Clone(), extra.MeshOffset.Clone());
                }
            }
            else
            {
                _coordinator.RefreshAttachmentSpheres(px, py, pz);
            }
        }

        private bool BeginFreeDrag(string sphereId, Point screen)
        {
            _ = sphereId;
            _ = screen;
            return false;
        }

        private void UpdateFreeDrag(Point screen)
        {
            _ = screen;
        }

        private void CommitDraggedSphereToUi()
        {
            if (_dragSphereId == null || _getDefinition == null)
            {
                return;
            }

            MachineDefinition def = _getDefinition();
            (double px, double py, double pz) = GetPreviewPose();

            if (MachineAttachmentSphereIds.IsMcs(_dragSphereId))
            {
                var mcs = def.McsZeroOffset ?? MachineGeometryPoint.Zero;
                _mcsOriginApplied?.Invoke(mcs.Clone());
            }
            else if (def.TryGetBuiltInNode(_dragSphereId) is MachineNodeDefinition node)
            {
                _attachApplied?.Invoke(_dragSphereId, node.AttachOnChild.Clone(), node.MeshOffset.Clone());
                Point3D assembly = MachineKinematics.GetAttachmentPointAssemblyMcs(def, _dragSphereId, px, py, pz);
                SphereDragCompleted?.Invoke(_dragSphereId, assembly);
            }
        }

        private (double X, double Y, double Z) GetPreviewPose() =>
            _getPreviewPose?.Invoke() ?? (0, 0, 0);

        private bool TryPickSphere(Point screen, out string? sphereId)
        {
            sphereId = null;
            (double px, double py, double pz) = GetPreviewPose();

            if (_coordinator.TryPickAttachmentSphereAtScreen(_viewport, screen, px, py, pz, out string rayHit))
            {
                sphereId = rayHit;
                return true;
            }

            Viewport3D? viewport3d = HelixViewportProjection.ResolveViewport3D(_viewport);
            if (viewport3d == null)
            {
                return false;
            }

            foreach (Viewport3DHelper.HitResult? hit in Viewport3DHelper.FindHits(viewport3d, screen)
                         .OrderBy(h => h.Distance))
            {
                if (hit == null)
                {
                    continue;
                }

                if (hit.Visual is Visual3D visual && _coordinator.TryPickAttachmentSphere(visual, out string id))
                {
                    sphereId = id;
                    return true;
                }

                if (_coordinator.TryPickAttachmentSphere(hit.Model, out string modelId))
                {
                    sphereId = modelId;
                    return true;
                }
            }

            return false;
        }

        private void SetHovered(string? sphereId)
        {
            if (string.Equals(_hoverSphereId, sphereId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoverSphereId = sphereId;
            _coordinator.SetAttachmentSphereHovered(sphereId);
        }
    }
}
