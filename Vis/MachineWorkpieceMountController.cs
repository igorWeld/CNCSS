using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;

namespace CNCSS.Vis
{
    public sealed class MachineWorkpieceMountController
    {
        private readonly HelixToolkit.Wpf.HelixViewport3D _viewport;
        private readonly MachineVisualCoordinator _coordinator;

        public MachineWorkpieceMountController(HelixToolkit.Wpf.HelixViewport3D viewport, MachineVisualCoordinator coordinator)
        {
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        public bool IsPickMode { get; set; }

        public string StatusText { get; private set; } = "Кликните по грани стола — плоскость установки заготовки.";

        public event Action? StateChanged;

        public event Action<MachineGeometryPoint>? WorkpieceMountApplied;

        public void ResetStatus()
        {
            StatusText = "Кликните по грани стола — плоскость установки заготовки.";
            StateChanged?.Invoke();
        }

        public bool TryHandleClick(System.Windows.Point screenPoint)
        {
            if (!IsPickMode)
            {
                return false;
            }

            if (!MeshFacePickHelper.TryPickMeshFace(_viewport, _coordinator, screenPoint, out MeshFacePick pick))
            {
                StatusText = "Грань не найдена. Кликните по модели стола.";
                StateChanged?.Invoke();
                return true;
            }

            if (!string.Equals(pick.NodeId, MachineNodeIds.Table, StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "Кликните по грани стола.";
                StateChanged?.Invoke();
                _coordinator.SetWorkpieceMountFaceMarker(null);
                return true;
            }

            Point3D centerLocal = _coordinator.GetMeshCenterLocal(MachineNodeIds.Table);
            MachineGeometryPoint mount = WorkpieceMountHelper.ComputeFromFacePlane(centerLocal, pick.PointMeshLocal);
            _coordinator.SetWorkpieceMountFaceMarker(pick);
            WorkpieceMountApplied?.Invoke(mount);
            StatusText = $"Плоскость: центр XY стола, Z={mount.Z:F2} мм (по грани). Оснастка — позже.";
            StateChanged?.Invoke();
            return true;
        }
    }
}
