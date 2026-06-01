using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;

namespace CNCSS.Vis
{
    public sealed class MachineSurfaceSnapController
    {
        private readonly HelixToolkit.Wpf.HelixViewport3D _viewport;
        private readonly MachineVisualCoordinator _coordinator;
        private MeshFacePick? _referenceFace;
        private MeshFacePick? _mobileFace;
        private MeshFacePick? _symmetryFaceA;
        private MeshFacePick? _symmetryFaceB;
        private SymmetricTranslationAxis _symmetryAxis = SymmetricTranslationAxis.Xy;
        private MachineFacePickTool _activeTool;

        public MachineSurfaceSnapController(HelixToolkit.Wpf.HelixViewport3D viewport, MachineVisualCoordinator coordinator)
        {
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        public MachineFacePickTool ActiveTool
        {
            get => _activeTool;
            set
            {
                _activeTool = value;
                ResetPicks();
            }
        }

        public bool IsSnapMode => _activeTool != MachineFacePickTool.None;

        public string StatusText { get; private set; } = "Выберите инструмент привязки.";

        public event Action? StateChanged;

        public event Action? AlignFacesCompleted;

        public event Action? SymmetricCenterCompleted;

        public event Action<string, MachineGeometryPoint, MachineGeometryPoint>? LayoutApplied;

        public void ResetPicks()
        {
            _referenceFace = null;
            _mobileFace = null;
            _symmetryFaceA = null;
            _symmetryFaceB = null;
            _symmetryAxis = SymmetricTranslationAxis.Xy;
            _coordinator.ClearFaceMarkers();
            StatusText = _activeTool switch
            {
                MachineFacePickTool.AlignFaces =>
                    "Совмещение: MCS — клик сферы MCS, затем грань станка; узел — грань детали, затем опора.",
                MachineFacePickTool.SymmetricCenter => "Симметрия: первая грань (1).",
                _ => "Выберите инструмент привязки."
            };
            StateChanged?.Invoke();
        }

        public bool TryHandleClick(System.Windows.Point screenPoint)
        {
            if (!IsSnapMode)
            {
                return false;
            }

            if (!MeshFacePickHelper.TryPickMeshFace(_viewport, _coordinator, screenPoint, out MeshFacePick pick))
            {
                StatusText = "Грань не найдена. Кликните по модели узла.";
                StateChanged?.Invoke();
                return true;
            }

            if (_activeTool == MachineFacePickTool.SymmetricCenter)
            {
                HandleSymmetricPick(pick);
            }
            else
            {
                HandleAlignPick(pick);
            }

            UpdateMarkers();
            StateChanged?.Invoke();
            return true;
        }

        private void HandleAlignPick(MeshFacePick pick)
        {
            if (_mobileFace == null)
            {
                _mobileFace = pick;
                StatusText = "Совмещение: 2) грань опоры на другой детали.";
                return;
            }

            if (_referenceFace == null)
            {
                if (string.Equals(pick.NodeId, _mobileFace.NodeId, StringComparison.OrdinalIgnoreCase))
                {
                    StatusText = "Выберите грань на другой детали (опора).";
                    return;
                }

                _referenceFace = pick;
                TryCompleteAlignSession();
                return;
            }

            _mobileFace = pick;
            _referenceFace = null;
            StatusText = "Совмещение: 2) грань опоры на другой детали.";
        }

        private void HandleSymmetricPick(MeshFacePick pick)
        {
            if (_symmetryFaceA == null)
            {
                _symmetryFaceA = pick;
                StatusText = "Симметрия: вторая грань (2).";
                return;
            }

            if (_symmetryFaceB == null)
            {
                _symmetryFaceB = pick;
                _symmetryAxis = MachineSymmetricAligner.DetectSymmetricAxis(_symmetryFaceA, _symmetryFaceB);
                StatusText = $"Симметрия: выберите деталь для центрирования ({FormatSymmetricAxisHint(_symmetryAxis)}).";
                return;
            }

            TryCompleteSymmetricSession(pick.NodeId);
        }

        private void UpdateMarkers() =>
            _coordinator.SetFaceMarkers(
                _activeTool == MachineFacePickTool.SymmetricCenter ? _symmetryFaceA : _referenceFace,
                _activeTool == MachineFacePickTool.SymmetricCenter ? _symmetryFaceB : _mobileFace);

        public bool TryAlignFaces() => TryCompleteAlignSession();

        public bool TrySymmetricCenter(string nodeId) => TryCompleteSymmetricSession(nodeId);

        private bool TryCompleteSymmetricSession(string nodeId)
        {
            MachineSurfaceAligner.MeshLayout? layout = SymmetricCenterCore(nodeId);
            if (layout == null)
            {
                return false;
            }

            _coordinator.ApplyMeshLayout(nodeId, layout.Offset, layout.RotationDegrees);
            LayoutApplied?.Invoke(nodeId, layout.Offset, layout.RotationDegrees);
            StatusText = $"Центр детали выставлен в середину между гранями ({FormatSymmetricAxisHint(_symmetryAxis)}).";
            _symmetryFaceA = null;
            _symmetryFaceB = null;
            _symmetryAxis = SymmetricTranslationAxis.Xy;
            _coordinator.ClearFaceMarkers();
            SymmetricCenterCompleted?.Invoke();
            StateChanged?.Invoke();
            return true;
        }

        private bool TryCompleteAlignSession()
        {
            MachineSurfaceAligner.MeshLayout? layout = AlignFacesCore();
            if (layout == null || _mobileFace == null)
            {
                _referenceFace = null;
                return false;
            }

            string nodeId = _mobileFace.NodeId;
            _coordinator.ApplyMeshLayout(nodeId, layout.Offset, layout.RotationDegrees);
            LayoutApplied?.Invoke(nodeId, layout.Offset, layout.RotationDegrees);
            StatusText = "Поверхности совмещены.";
            _referenceFace = null;
            _mobileFace = null;
            _coordinator.ClearFaceMarkers();
            AlignFacesCompleted?.Invoke();
            StateChanged?.Invoke();
            return true;
        }

        private MachineSurfaceAligner.MeshLayout? AlignFacesCore()
        {
            if (_referenceFace == null || _mobileFace == null)
            {
                StatusText = "Сначала выберите две грани.";
                StateChanged?.Invoke();
                return null;
            }

            string mobileNodeId = _mobileFace.NodeId;
            if (!_coordinator.TryGetNodeSlotInfo(mobileNodeId, out NodeSlotInfo slot))
            {
                StatusText = "Не удалось определить узел для совмещения.";
                StateChanged?.Invoke();
                return null;
            }

            return MachineSurfaceAligner.AlignMobileFaceToReference(
                _referenceFace,
                _mobileFace,
                slot.KinematicTransform,
                slot.MeshTransform);
        }

        private MachineSurfaceAligner.MeshLayout? SymmetricCenterCore(string nodeId)
        {
            if (_symmetryFaceA == null || _symmetryFaceB == null)
            {
                StatusText = "Сначала выберите две грани.";
                StateChanged?.Invoke();
                return null;
            }

            if (!_coordinator.TryGetNodeSlotInfo(nodeId, out NodeSlotInfo slot))
            {
                StatusText = "Не удалось определить выбранную деталь.";
                StateChanged?.Invoke();
                return null;
            }

            Point3D meshCenterLocal = _coordinator.GetMeshCenterLocal(nodeId);
            return MachineSymmetricAligner.CenterMeshOnFaceMidpointXY(
                _symmetryFaceA,
                _symmetryFaceB,
                meshCenterLocal,
                slot.KinematicTransform,
                slot.MeshTransform);
        }

        private static string FormatSymmetricAxisHint(SymmetricTranslationAxis axis) => axis switch
        {
            SymmetricTranslationAxis.X => "по оси X",
            SymmetricTranslationAxis.Y => "по оси Y",
            SymmetricTranslationAxis.Z => "по оси Z",
            SymmetricTranslationAxis.X | SymmetricTranslationAxis.Y => "по осям X и Y",
            _ => "по осям X и Y"
        };
    }
}
