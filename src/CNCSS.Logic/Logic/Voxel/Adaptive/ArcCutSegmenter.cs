using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Logic.Voxel.Adaptive;

/// <summary>Аппроксимация дуг G2/G3 линейными сегментами под заданную максимальную хорду.</summary>
public static class ArcCutSegmenter
{
    public static IReadOnlyList<VoxelMotionSegment> SegmentArc(
        ArcGeometry arc,
        int lineNumber,
        bool isCuttingMove,
        double maxChordMm = 0.05)
    {
        if (arc.Radius <= 1e-6 || Math.Abs(arc.SweepAngleRad) <= 1e-9)
        {
            return new[]
            {
                new VoxelMotionSegment(
                    new Point3D(arc.StartX, arc.StartY, arc.StartZ),
                    new Point3D(arc.EndX, arc.EndY, arc.EndZ),
                    isCuttingMove,
                    lineNumber)
            };
        }

        double maxChord = Math.Max(1e-4, maxChordMm);
        double ratio = Math.Clamp(1.0 - maxChord / arc.Radius, -1.0, 1.0);
        double stepAngle = 2.0 * Math.Acos(ratio);
        if (double.IsNaN(stepAngle) || stepAngle <= 1e-9)
        {
            stepAngle = Math.Abs(arc.SweepAngleRad);
        }

        int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(arc.SweepAngleRad) / stepAngle));
        var result = new List<VoxelMotionSegment>(segments);

        Point3D prev = new(arc.StartX, arc.StartY, arc.StartZ);
        for (int i = 1; i <= segments; i++)
        {
            double t = (double)i / segments;
            Point3D next = Evaluate(arc, t);
            result.Add(new VoxelMotionSegment(prev, next, isCuttingMove, lineNumber));
            prev = next;
        }

        return result;
    }

    private static Point3D Evaluate(ArcGeometry arc, double t)
    {
        double angle = arc.StartAngleRad + arc.SweepAngleRad * t;
        double u = arc.CenterU + arc.Radius * Math.Cos(angle);
        double v = arc.CenterV + arc.Radius * Math.Sin(angle);
        double x = arc.StartX + (arc.EndX - arc.StartX) * t;
        double y = arc.StartY + (arc.EndY - arc.StartY) * t;
        double z = arc.StartZ + (arc.EndZ - arc.StartZ) * t;

        return arc.Plane switch
        {
            17 => new Point3D(u, v, z),
            18 => new Point3D(u, y, v),
            19 => new Point3D(x, u, v),
            _ => new Point3D(x, y, z)
        };
    }
}
