using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Ray pick and projection helpers for RGB axis gizmos.</summary>
    internal static class GizmoAxisDragMath
    {
        public const double PickDistanceMm = 24;

        public static bool TryGetCursorRay(HelixViewport3D viewport, Point screenPoint, out Ray3D ray)
        {
            ray = new Ray3D();
            if (viewport.Camera is not ProjectionCamera camera)
            {
                return false;
            }

            Point3D origin = camera.Position;
            Point3D? target = viewport.FindNearestPoint(screenPoint);
            if (!target.HasValue)
            {
                target = HelixViewportProjection.UnProject(viewport, screenPoint);
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

        public static bool TryPickWorldAxis(
            Ray3D ray,
            Point3D axisOriginWorld,
            double arrowLengthMm,
            out GizmoAxis axis)
        {
            axis = default;
            double best = PickDistanceMm;
            bool found = false;

            foreach (GizmoAxis candidate in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
            {
                Vector3D dir = ToUnitAxis(candidate);
                Point3D tip = axisOriginWorld + dir * arrowLengthMm;
                double dist = DistanceRayToSegment(ray, axisOriginWorld, tip);
                if (dist < best)
                {
                    best = dist;
                    axis = candidate;
                    found = true;
                }
            }

            return found;
        }

        public static double ProjectMouseOntoAxis(
            HelixViewport3D viewport,
            Point screenPoint,
            Point3D axisOriginWorld,
            Vector3D axisDirWorld)
        {
            if (!TryGetCursorRay(viewport, screenPoint, out Ray3D ray))
            {
                return 0;
            }

            Vector3D u = axisDirWorld;
            if (u.LengthSquared < 1e-12)
            {
                return 0;
            }

            u.Normalize();
            Vector3D w = ray.Origin - axisOriginWorld;
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

        public static Vector3D ToUnitAxis(GizmoAxis axis) =>
            axis switch
            {
                GizmoAxis.X => new Vector3D(1, 0, 0),
                GizmoAxis.Y => new Vector3D(0, 1, 0),
                _ => new Vector3D(0, 0, 1)
            };

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
    }
}
