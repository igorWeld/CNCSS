using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Geometry.BRep;

namespace CNCSS.Geometry.SweptVolume;

/// <summary>След режущей части (L_cut, R_cut) без хвостовика; 3-осевой: ось || +Z.</summary>
public sealed class CylinderSweepField : ISolidField
{
    private readonly Point3D _start;
    private readonly Point3D _end;
    private readonly double _radius;
    private readonly double _fluteLength;
    private readonly Vector3D _dir;
    private readonly double _len2;

    private const double FaceCutVoxelPadMm = 0.10;

    public CylinderSweepField(Point3D start, Point3D end, double radius, double fluteLength)
    {
        _start = start;
        _end = end;
        _radius = radius;
        _fluteLength = fluteLength;
        _dir = end - start;
        _len2 = _dir.X * _dir.X + _dir.Y * _dir.Y + _dir.Z * _dir.Z;
        double ex = radius + FaceCutVoxelPadMm;
        Bounds = new Rect3D(
            Math.Min(start.X, end.X) - ex,
            Math.Min(start.Y, end.Y) - ex,
            Math.Min(start.Z, end.Z) - FaceCutVoxelPadMm,
            Math.Abs(end.X - start.X) + 2 * ex,
            Math.Abs(end.Y - start.Y) + 2 * ex,
            Math.Abs(end.Z - start.Z) + fluteLength + FaceCutVoxelPadMm + 2e-6);
    }

    public Rect3D Bounds { get; }

    public bool Contains(Point3D p)
    {
        double t = 0.0;
        if (_len2 > ProjectConstants.EPSILON)
        {
            t = Math.Clamp(
                ((p.X - _start.X) * _dir.X + (p.Y - _start.Y) * _dir.Y + (p.Z - _start.Z) * _dir.Z)
                / (_len2 + ProjectConstants.SMALL_EPSILON),
                0.0,
                1.0);
        }

        double cx = _start.X + t * _dir.X;
        double cy = _start.Y + t * _dir.Y;
        double tipZ = _start.Z + t * _dir.Z;
        double dx = p.X - cx;
        double dy = p.Y - cy;
        double rEff = _radius + FaceCutVoxelPadMm;
        if (dx * dx + dy * dy > rEff * rEff)
        {
            return false;
        }

        // Торец снимает слой ниже tipZ; стружечная часть — [tipZ, tipZ + L_cut] вдоль +Z.
        return p.Z >= tipZ - FaceCutVoxelPadMm && p.Z <= tipZ + _fluteLength;
    }
}
