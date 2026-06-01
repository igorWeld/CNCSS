using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Geometry.BRep;

namespace CNCSS.Geometry.SweptVolume;

public sealed class CylinderSweepField : ISolidField
{
    private readonly Point3D _start;
    private readonly Point3D _end;
    private readonly double _radius;
    private readonly double _fluteLength;
    private readonly Vector3D _dir;
    private readonly double _len2xy;

    public CylinderSweepField(Point3D start, Point3D end, double radius, double fluteLength)
    {
        _start = start;
        _end = end;
        _radius = radius;
        _fluteLength = fluteLength;
        _dir = end - start;
        _len2xy = _dir.X * _dir.X + _dir.Y * _dir.Y;
        double ex = radius + 1e-6;
        Bounds = new Rect3D(
            Math.Min(start.X, end.X) - ex,
            Math.Min(start.Y, end.Y) - ex,
            Math.Min(start.Z, end.Z) - 1e-6,
            Math.Abs(end.X - start.X) + 2 * ex,
            Math.Abs(end.Y - start.Y) + 2 * ex,
            Math.Abs(end.Z - start.Z) + fluteLength + 2e-6);
    }

    public Rect3D Bounds { get; }

    public bool Contains(Point3D p)
    {
        double t = _len2xy < ProjectConstants.EPSILON
            ? 0.0
            : Math.Clamp(((p.X - _start.X) * _dir.X + (p.Y - _start.Y) * _dir.Y) / (_len2xy + ProjectConstants.SMALL_EPSILON), 0.0, 1.0);

        double cx = _start.X + t * _dir.X;
        double cy = _start.Y + t * _dir.Y;
        double tipZ = _start.Z + t * _dir.Z;
        double dx = p.X - cx;
        double dy = p.Y - cy;
        if (dx * dx + dy * dy > _radius * _radius)
        {
            return false;
        }

        return p.Z >= tipZ && p.Z <= tipZ + _fluteLength;
    }
}
