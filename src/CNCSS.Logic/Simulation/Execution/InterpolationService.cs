using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Simulation.Execution
{
    /// <summary>Чистая логика прогресса интерполяции между точками с учётом подачи и множителя скорости.</summary>
    public static class InterpolationService
    {
        public static (double progress, int intervalMs) InitializeSegment(Point3D start, Point3D target)
        {
            double dist = (target - start).Length;
            if (dist < 0.0001)
            {
                return (1.0, 100);
            }

            return (0.0, 10);
        }

        public static double AdvanceProgress(
            double currentProgress,
            Point3D start,
            Point3D target,
            double speedMmPerSec,
            double simulationMultiplier,
            double fpsSlowdownFactor)
        {
            if (currentProgress >= 1.0)
            {
                return 1.0;
            }

            double dist = (target - start).Length;
            if (dist <= 0)
            {
                return 1.0;
            }

            double safeSpeed = speedMmPerSec <= 0 ? 0.001 : speedMmPerSec;
            double step = (safeSpeed * 0.01 * simulationMultiplier * fpsSlowdownFactor) / dist;
            return Math.Min(1.0, currentProgress + step);
        }

        public static Point3D ComputePosition(Point3D start, Point3D target, double progress, ArcGeometry? arc)
        {
            if (arc != null && progress < 1.0)
            {
                double currentAngle = arc.StartAngleRad + arc.SweepAngleRad * progress;
                double u = arc.CenterU + arc.Radius * Math.Cos(currentAngle);
                double v = arc.CenterV + arc.Radius * Math.Sin(currentAngle);
                double t = progress;

                return arc.Plane switch
                {
                    17 => new Point3D(u, v, arc.StartZ + (arc.EndZ - arc.StartZ) * t),
                    18 => new Point3D(u, arc.StartY + (arc.EndY - arc.StartY) * t, v),
                    19 => new Point3D(arc.StartX + (arc.EndX - arc.StartX) * t, u, v),
                    _ => Lerp(start, target, progress)
                };
            }

            return Lerp(start, target, progress);
        }

        private static Point3D Lerp(Point3D start, Point3D target, double t)
        {
            return new Point3D(
                start.X + (target.X - start.X) * t,
                start.Y + (target.Y - start.Y) * t,
                start.Z + (target.Z - start.Z) * t);
        }
    }
}
