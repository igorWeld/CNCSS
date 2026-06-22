using System.Windows.Input;

using System.Windows.Media.Media3D;

using CNCSS.Machine.Model;



namespace CNCSS.Vis

{

    public sealed class MachineToolMountController

    {

        private readonly HelixToolkit.Wpf.HelixViewport3D _viewport;

        private readonly MachineVisualCoordinator _coordinator;

        private string _toolMountNodeId = MachineNodeIds.Spindle;

        private bool _isDragging;

        private Func<MachineGeometryPoint>? _getCurrentMount;



        public MachineToolMountController(HelixToolkit.Wpf.HelixViewport3D viewport, MachineVisualCoordinator coordinator)

        {

            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

        }



        public bool IsPickZMode { get; set; }



        public bool IsInteractionEnabled { get; set; }



        public bool IsDragging => _isDragging;



        public string StatusText { get; private set; } = "Перетащите точку по модели шпинделя или задайте Z по грани.";



        public event Action? StateChanged;



        public event Action<string, MachineGeometryPoint>? ToolMountApplied;



        public void SetToolMountNodeId(string nodeId) =>

            _toolMountNodeId = string.IsNullOrWhiteSpace(nodeId) ? MachineNodeIds.Spindle : nodeId;



        public void SetCurrentMountProvider(Func<MachineGeometryPoint> provider) =>

            _getCurrentMount = provider ?? throw new ArgumentNullException(nameof(provider));



        public void ResetStatus()

        {

            StatusText = "Перетащите точку по модели шпинделя или задайте Z по грани.";

            StateChanged?.Invoke();

        }



        public bool TryHandleClick(System.Windows.Point screenPoint)

        {

            if (!IsPickZMode)

            {

                return false;

            }



            if (!TryPickOnToolMountNode(screenPoint, out MeshFacePick pick))

            {

                StatusText = "Грань не найдена. Кликните по модели шпинделя.";

                StateChanged?.Invoke();

                return true;

            }



            MachineDefinition def = _coordinator.GetDefinition();
            (double px, double py, double pz) = _coordinator.GetPreviewPhysicalPose();
            MachineGeometryPoint current = _getCurrentMount?.Invoke() ?? MachineGeometryPoint.Zero;
            MachineGeometryPoint mount = ToolMountMcsHelper.SetTcpMcsZFromWorldPick(def, pick.PointWorld, px, py, pz, current);
            _coordinator.SetToolMountFaceMarker(pick);
            ToolMountApplied?.Invoke(_toolMountNodeId, mount);
            StatusText = $"TCP Z={mount.Z:F2} мм (MCS, по выбранной грани).";

            StateChanged?.Invoke();

            return true;

        }



        public bool TryBeginDrag(System.Windows.Point screenPoint)

        {

            if (!IsInteractionEnabled || IsPickZMode || _isDragging)

            {

                return false;

            }



            if (!TryPickOnToolMountNode(screenPoint, out MeshFacePick pick))

            {

                return false;

            }



            _isDragging = true;

            ApplyPickAsMount(pick);

            _viewport.CaptureMouse();

            StateChanged?.Invoke();

            return true;

        }



        public bool TryUpdateDrag(System.Windows.Point screenPoint)

        {

            if (!_isDragging)

            {

                return false;

            }



            if (!TryPickOnToolMountNode(screenPoint, out MeshFacePick pick))

            {

                return true;

            }



            ApplyPickAsMount(pick);

            return true;

        }



        public void EndDrag()

        {

            if (!_isDragging)

            {

                return;

            }



            _isDragging = false;

            if (_viewport.IsMouseCaptured)

            {

                _viewport.ReleaseMouseCapture();

            }



            StateChanged?.Invoke();

        }



        private void ApplyPickAsMount(MeshFacePick pick)

        {

            MachineDefinition def = _coordinator.GetDefinition();
            (double px, double py, double pz) = _coordinator.GetPreviewPhysicalPose();
            MachineGeometryPoint mount = ToolMountMcsHelper.InferToolMountMcsFromWorldTcp(def, pick.PointWorld, px, py, pz);
            _coordinator.SetToolMountFaceMarker(null);
            ToolMountApplied?.Invoke(_toolMountNodeId, mount);
            StatusText = $"TCP (MCS): {mount.X:F1}, {mount.Y:F1}, {mount.Z:F1} мм";

            StateChanged?.Invoke();

        }



        private bool TryPickOnToolMountNode(System.Windows.Point screenPoint, out MeshFacePick pick)

        {

            pick = null!;

            if (!MeshFacePickHelper.TryPickMeshFace(_viewport, _coordinator, screenPoint, out MeshFacePick candidate))

            {

                return false;

            }



            if (!string.Equals(candidate.NodeId, _toolMountNodeId, StringComparison.OrdinalIgnoreCase))

            {

                _coordinator.SetToolMountFaceMarker(null);

                return false;

            }



            pick = candidate;

            return true;

        }

    }

}

