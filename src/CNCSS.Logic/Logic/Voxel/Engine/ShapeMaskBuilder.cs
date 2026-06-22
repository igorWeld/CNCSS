using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Logic.Voxel.Engine;

public static class ShapeMaskBuilder
{
  public static IShapeMaskField Build(StockVolumeConfig volume, StockConstructorConfig? constructor)
  {
    double sizeX = Math.Max(1e-6, volume.MaxX - volume.MinX);
    double sizeY = Math.Max(1e-6, volume.MaxY - volume.MinY);
    double sizeZ = Math.Max(1e-6, volume.MaxZ - volume.MinZ);
    double cx = sizeX * 0.5;
    double cy = sizeY * 0.5;

    if (constructor == null)
    {
      return new BoxShapeMask(sizeX, sizeY, sizeZ);
    }

    return constructor.ShapeType switch
    {
      StockShapeType.Rectangular => new BoxShapeMask(
        Math.Min(sizeX, Math.Max(1e-6, constructor.Param1Mm)),
        Math.Min(sizeY, Math.Max(1e-6, constructor.Param2Mm)),
        Math.Min(sizeZ, Math.Max(1e-6, constructor.Param3Mm))),
      StockShapeType.Round => new CylinderShapeMask(cx, cy, constructor.Param1Mm * 0.5, sizeZ),
      StockShapeType.Tube => new TubeShapeMask(
        cx,
        cy,
        constructor.Param1Mm * 0.5,
        constructor.Param2Mm * 0.5,
        sizeZ),
      StockShapeType.Hexagonal => new HexShapeMask(cx, cy, constructor.Param1Mm * 0.5, sizeZ),
      _ => new BoxShapeMask(sizeX, sizeY, sizeZ)
    };
  }

  private sealed class BoxShapeMask(double sizeX, double sizeY, double sizeZ) : IShapeMaskField
  {
    public bool IsFullBoundingBox => true;

    public bool Contains(Point3D p) =>
      p.X >= 0 && p.X <= sizeX + 1e-6 &&
      p.Y >= 0 && p.Y <= sizeY + 1e-6 &&
      p.Z >= 0 && p.Z <= sizeZ + 1e-6;

    public bool IsNearBoundary(Point3D p, double marginMm)
    {
      if (!Contains(p))
      {
        return false;
      }

      return p.X < marginMm || p.Y < marginMm || p.Z < marginMm ||
             p.X > sizeX - marginMm || p.Y > sizeY - marginMm || p.Z > sizeZ - marginMm;
    }
  }

  private sealed class CylinderShapeMask(double cx, double cy, double radius, double height) : IShapeMaskField
  {
    private readonly double _r2 = radius * radius;

    public bool IsFullBoundingBox => false;

    public bool Contains(Point3D p)
    {
      if (p.Z < 0 || p.Z > height + 1e-6)
      {
        return false;
      }

      double dx = p.X - cx;
      double dy = p.Y - cy;
      return dx * dx + dy * dy <= _r2 + 1e-6;
    }

    public bool IsNearBoundary(Point3D p, double marginMm)
    {
      if (!Contains(p))
      {
        return false;
      }

      if (p.Z < marginMm || p.Z > height - marginMm)
      {
        return true;
      }

      double dx = p.X - cx;
      double dy = p.Y - cy;
      double dist = Math.Sqrt(dx * dx + dy * dy);
      return dist > radius - marginMm;
    }
  }

  private sealed class TubeShapeMask(double cx, double cy, double rOuter, double rInner, double height) : IShapeMaskField
  {
    private readonly double _outer2 = rOuter * rOuter;
    private readonly double _inner2 = rInner * rInner;

    public bool IsFullBoundingBox => false;

    public bool Contains(Point3D p)
    {
      if (p.Z < 0 || p.Z > height + 1e-6)
      {
        return false;
      }

      double dx = p.X - cx;
      double dy = p.Y - cy;
      double d2 = dx * dx + dy * dy;
      return d2 <= _outer2 + 1e-6 && d2 >= _inner2 - 1e-6;
    }

    public bool IsNearBoundary(Point3D p, double marginMm)
    {
      if (!Contains(p))
      {
        return false;
      }

      if (p.Z < marginMm || p.Z > height - marginMm)
      {
        return true;
      }

      double dx = p.X - cx;
      double dy = p.Y - cy;
      double dist = Math.Sqrt(dx * dx + dy * dy);
      return dist > rOuter - marginMm || dist < rInner + marginMm;
    }
  }

  private sealed class HexShapeMask : IShapeMaskField
  {
    private readonly double[] _vx;
    private readonly double[] _vy;
    private readonly double _height;

    public bool IsFullBoundingBox => false;

    public HexShapeMask(double cx, double cy, double apothem, double height)
    {
      _height = height;
      double r = apothem / Math.Cos(Math.PI / 6.0);
      _vx = new double[6];
      _vy = new double[6];
      for (int i = 0; i < 6; i++)
      {
        double a = Math.PI / 6.0 + i * Math.PI / 3.0;
        _vx[i] = cx + Math.Cos(a) * r;
        _vy[i] = cy + Math.Sin(a) * r;
      }
    }

    public bool Contains(Point3D p)
    {
      if (p.Z < 0 || p.Z > _height + 1e-6)
      {
        return false;
      }

      return PointInConvexHex(p.X, p.Y);
    }

    public bool IsNearBoundary(Point3D p, double marginMm)
    {
      if (!Contains(p))
      {
        return false;
      }

      if (p.Z < marginMm || p.Z > _height - marginMm)
      {
        return true;
      }

      for (int i = 0; i < 6; i++)
      {
        int j = (i + 1) % 6;
        double edgeDx = _vx[j] - _vx[i];
        double edgeDy = _vy[j] - _vy[i];
        double len = Math.Sqrt(edgeDx * edgeDx + edgeDy * edgeDy);
        if (len < 1e-9)
        {
          continue;
        }

        double dist = Math.Abs(edgeDx * (p.Y - _vy[i]) - edgeDy * (p.X - _vx[i])) / len;
        if (dist < marginMm)
        {
          return true;
        }
      }

      return false;
    }

    private bool PointInConvexHex(double x, double y)
    {
      bool sign = false;
      for (int i = 0; i < 6; i++)
      {
        int j = (i + 1) % 6;
        double cross = (_vx[j] - _vx[i]) * (y - _vy[i]) - (_vy[j] - _vy[i]) * (x - _vx[i]);
        if (Math.Abs(cross) < 1e-9)
        {
          continue;
        }

        if (!sign)
        {
          sign = cross > 0;
        }
        else if ((cross > 0) != sign)
        {
          return false;
        }
      }

      return true;
    }
  }
}
