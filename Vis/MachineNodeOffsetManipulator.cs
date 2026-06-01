using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Drag X/Y/Z gizmos at mesh center to edit node MeshOffset in machine setup preview.</summary>
    public sealed class MachineNodeOffsetManipulator
    {
        private const double PickDistanceMm = 24;
        private readonly HelixViewport3D _viewport;
        private readonly MachineVisualCoordinator _coordinator;
        private string _selectedNodeId = MachineNodeIds.Table;
        private bool _isDragging;
        private GizmoAxis _dragAxis;
        private string _dragNodeId = MachineNodeIds.Table;
        private Point3D _dragAxisOriginWorld;
        private Vector3D _dragAxisDirectionWorld;
        private double _dragStartProjection;
        private double _offsetStartX;
        private double _offsetStartY;
        private double _offsetStartZ;
        private double _rotationStartX;
        private double _rotationStartY;
        private double _rotationStartZ;
        private bool _dragRotates;
        private Action<string, double, double, double>? _offsetChanged;
        private Action<string, double, double, double>? _rotationChanged;
        private Func<bool>? _isSurfaceSnapMode;

        public MachineNodeOffsetManipulator(HelixViewport3D viewport, MachineVisualCoordinator coordinator)
        {
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _viewport.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            _viewport.PreviewMouseMove += OnPreviewMouseMove;
            _viewport.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        }

        public string SelectedNodeId
        {
            get => _selectedNodeId;
            set
            {
                _selectedNodeId = string.IsNullOrWhiteSpace(value) ? MachineNodeIds.Table : value;
                _coordinator.SetActiveGizmoNode(_selectedNodeId);
            }
        }

        public bool IsDragging => _isDragging;

        public void SetOffsetChangedHandler(Action<string, double, double, double> handler) =>
            _offsetChanged = handler;

        public void SetRotationChangedHandler(Action<string, double, double, double> handler) =>
            _rotationChanged = handler;

        public void SetSurfaceSnapModeProvider(Func<bool> provider) => _isSurfaceSnapMode = provider;

        public bool IsLayoutEditEnabled => _coordinator.IsNodeLayoutEditMode;

        public void RefreshGizmos() => _coordinator.RefreshGizmos();

        public void Detach()
        {
            _viewport.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            _viewport.PreviewMouseMove -= OnPreviewMouseMove;
            _viewport.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        }

        public bool TryBeginAxisDrag(Point screenPoint)
        {
            if (_isSurfaceSnapMode?.Invoke() == true || !_coordinator.IsNodeLayoutEditMode)
            {
                return false;
            }

            if (!TryPickAxis(screenPoint, _selectedNodeId, out GizmoAxis axis))
            {
                return false;
            }

            return BeginAxisDrag(screenPoint, axis);
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isSurfaceSnapMode?.Invoke() == true || !_coordinator.IsNodeLayoutEditMode)
            {
                return;
            }

            if (!TryPickAxis(e.GetPosition(_viewport), _selectedNodeId, out GizmoAxis axis))
            {
                return;
            }

            if (BeginAxisDrag(e.GetPosition(_viewport), axis))
            {
                e.Handled = true;
            }
        }

        private bool BeginAxisDrag(Point screenPoint, GizmoAxis axis)
        {
            _dragRotates = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
            if (_dragRotates)
            {
                if (!_coordinator.TryGetNodeRotation(_selectedNodeId, out double rx, out double ry, out double rz))
                {
                    return false;
                }

                _rotationStartX = rx;
                _rotationStartY = ry;
                _rotationStartZ = rz;
            }
            else if (!_coordinator.TryGetNodeOffset(_selectedNodeId, out double ox, out double oy, out double oz))
            {
                return false;
            }
            else
            {
                _offsetStartX = ox;
                _offsetStartY = oy;
                _offsetStartZ = oz;
            }

            Point3D gizmoCenter = _coordinator.GetGizmoCenterWorld(_selectedNodeId);
            Vector3D axisDir = GetWorldAxisDirection(_selectedNodeId, axis);
            if (axisDir.LengthSquared < 1e-12)
            {
                return false;
            }

            axisDir.Normalize();
            _isDragging = true;
            _dragAxis = axis;
            _dragNodeId = _selectedNodeId;
            _dragAxisOriginWorld = gizmoCenter;
            _dragAxisDirectionWorld = axisDir;
            _dragStartProjection = ProjectMouseOntoAxis(screenPoint, gizmoCenter, axisDir);
            _viewport.CaptureMouse();
            return true;
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            double projection = ProjectMouseOntoAxis(e.GetPosition(_viewport), _dragAxisOriginWorld, _dragAxisDirectionWorld);
            double delta = projection - _dragStartProjection;

            if (_dragRotates)
            {
                double rx = _rotationStartX;
                double ry = _rotationStartY;
                double rz = _rotationStartZ;
                double deltaDeg = delta * 0.35;
                switch (_dragAxis)
                {
                    case GizmoAxis.X:
                        rx = _rotationStartX + deltaDeg;
                        break;
                    case GizmoAxis.Y:
                        ry = _rotationStartY + deltaDeg;
                        break;
                    case GizmoAxis.Z:
                        rz = _rotationStartZ + deltaDeg;
                        break;
                }

                _coordinator.SetNodeRotation(_dragNodeId, rx, ry, rz);
                _rotationChanged?.Invoke(_dragNodeId, rx, ry, rz);
            }
            else
            {
                Vector3D offsetDelta = GetMeshOffsetDeltaForAxis(_dragNodeId, _dragAxis, delta);
                double ox = _offsetStartX + offsetDelta.X;
                double oy = _offsetStartY + offsetDelta.Y;
                double oz = _offsetStartZ + offsetDelta.Z;

                _coordinator.SetNodeOffset(_dragNodeId, ox, oy, oz);
                _offsetChanged?.Invoke(_dragNodeId, ox, oy, oz);
            }

            e.Handled = true;
        }

        private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            _isDragging = false;
            _viewport.ReleaseMouseCapture();
            e.Handled = true;
        }

        private bool TryPickAxis(Point screenPoint, string nodeId, out GizmoAxis axis)
        {
            axis = default;
            if (!TryGetCursorRay(screenPoint, out Ray3D ray))
            {
                return false;
            }

            double best = PickDistanceMm;
            bool found = false;
            Point3D origin = _coordinator.GetGizmoCenterWorld(nodeId);
            double arrowLength = _coordinator.GetGizmoArrowLengthMm(nodeId);

            foreach (GizmoAxis candidate in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
            {
                Vector3D dir = GetWorldAxisDirection(nodeId, candidate);
                if (dir.LengthSquared < 1e-12)
                {
                    continue;
                }

                dir.Normalize();
                Point3D tip = origin + dir * arrowLength;
                double dist = DistanceRayToSegment(ray, origin, tip);
                if (dist < best)
                {
                    best = dist;
                    axis = candidate;
                    found = true;
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

            double sc;
            double tc;
            if (denom < 1e-9)
            {
                sc = 0;
                tc = e / c;
            }
            else
            {
                sc = (b * e - c * d) / denom;
                tc = (a * e - b * d) / denom;
            }

            tc = Math.Clamp(tc, 0, 1);
            Point3D pointOnSegment = segStart + tc * v;
            Point3D pointOnRay = ray.Origin + sc * u;
            return (pointOnSegment - pointOnRay).Length;
        }

        private double ProjectMouseOntoAxis(Point screenPoint, Point3D axisOrigin, Vector3D axisDir)
        {
            if (!TryGetCursorRay(screenPoint, out Ray3D ray))
            {
                return 0;
            }

            Vector3D u = axisDir;
            u.Normalize();
            Vector3D w = ray.Origin - axisOrigin;
            Vector3D d = ray.Direction;
            if (d.LengthSquared < 1e-12)
            {
                return Vector3D.DotProduct(w, u);
            }

            d.Normalize();
            double a = Vector3D.DotProduct(u, u);
            double b = Vector3D.DotProduct(u, d);
            double c = Vector3D.DotProduct(d, d);
            double dVal = Vector3D.DotProduct(u, w);
            double e = Vector3D.DotProduct(d, w);
            double denom = a * c - b * b;
            if (Math.Abs(denom) < 1e-9)
            {
                return dVal / a;
            }

            return (b * e - c * dVal) / denom;
        }

        private bool TryGetCursorRay(Point screenPoint, out Ray3D ray)
        {
            ray = new Ray3D();
            if (_viewport.Camera is not ProjectionCamera camera)
            {
                return false;
            }

            Point3D origin = camera.Position;
            Point3D? target = _viewport.FindNearestPoint(screenPoint);
            if (!target.HasValue)
            {
                target = HelixViewportProjection.UnProject(_viewport, screenPoint);
            }

            if (!target.HasValue)
            {
                return false;
            }

            Vector3D dir = target.Value - origin;
            if (dir.LengthSquared < 1e-12)
            {
                return false;
            }

            dir.Normalize();
            ray = new Ray3D(origin, dir);
            return true;
        }

        private Vector3D GetWorldAxisDirection(string nodeId, GizmoAxis axis)
        {
            Transform3D kinematic = _coordinator.GetKinematicTransform(nodeId);
            Vector3D meshAxis = Transform3DHelper.TransformVector(
                _coordinator.GetMeshTransform(nodeId),
                ToUnitAxis(axis));

            Vector3D world = Transform3DHelper.TransformVector(kinematic, meshAxis);
            if (world.LengthSquared < 1e-12)
            {
                return world;
            }

            world.Normalize();
            return world;
        }

        /// <summary>Offset lives in parent space after mesh rotation; delta along dragged arrow is R·ê·Δ.</summary>
        private Vector3D GetMeshOffsetDeltaForAxis(string nodeId, GizmoAxis axis, double delta)
        {
            Transform3D mesh = _coordinator.GetMeshTransform(nodeId);
            return Transform3DHelper.TransformVector(mesh, ToUnitAxis(axis) * delta);
        }

        private static Vector3D ToUnitAxis(GizmoAxis axis) =>
            axis switch
            {
                GizmoAxis.X => new Vector3D(1, 0, 0),
                GizmoAxis.Y => new Vector3D(0, 1, 0),
                _ => new Vector3D(0, 0, 1)
            };
    }

    internal static class Transform3DHelper
    {
        public static Vector3D TransformVector(Transform3D transform, Vector3D vector)
        {
            if (transform == null || transform == Transform3D.Identity)
            {
                return vector;
            }

            if (transform is MatrixTransform3D matrixTransform)
            {
                return matrixTransform.Value.Transform(vector);
            }

            if (transform is Transform3DGroup group)
            {
                Vector3D result = vector;
                foreach (Transform3D child in group.Children)
                {
                    result = TransformVector(child, result);
                }

                return result;
            }

            return vector;
        }
    }
}
