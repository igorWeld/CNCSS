using System.Windows.Media.Media3D;
using CNCSS.Geometry.BRep;
using CNCSS.Vis;

namespace CNCSS.Geometry.Boolean;

public static class BooleanSubtract
{
    public static BrepSolid Subtract(BrepSolid stock, BrepSolid swept, double gridStepMm)
    {
        Rect3D b = stock.Field.Bounds;
        int nx = Math.Max(1, (int)Math.Ceiling(b.SizeX / gridStepMm));
        int ny = Math.Max(1, (int)Math.Ceiling(b.SizeY / gridStepMm));
        int nz = Math.Max(1, (int)Math.Ceiling(b.SizeZ / gridStepMm));
        byte[] occ = GC.AllocateUninitializedArray<byte>(checked(nx * ny * nz));

        int idx = 0;
        for (int iz = 0; iz < nz; iz++)
        {
            double z0 = b.Z + iz * gridStepMm;
            for (int iy = 0; iy < ny; iy++)
            {
                double y0 = b.Y + iy * gridStepMm;
                for (int ix = 0; ix < nx; ix++)
                {
                    double x0 = b.X + ix * gridStepMm;
                    Point3D center = new(x0 + 0.5 * gridStepMm, y0 + 0.5 * gridStepMm, z0 + 0.5 * gridStepMm);
                    bool hasStock = stock.Field.Contains(center);
                    bool cutHit = CellIntersectsField(swept.Field, x0, y0, z0, gridStepMm);
                    occ[idx++] = (byte)(hasStock && !cutHit ? 1 : 0);
                }
            }
        }

        // Паддинг для внешней оболочки.
        int px = nx + 2;
        int py = ny + 2;
        int pz = nz + 2;
        byte[] padded = GC.AllocateUninitializedArray<byte>(checked(px * py * pz));
        for (int z = 0; z < nz; z++)
        {
            int srcBase = z * nx * ny;
            int dstBase = (z + 1) * px * py;
            for (int y = 0; y < ny; y++)
            {
                occ.AsSpan(srcBase + y * nx, nx).CopyTo(padded.AsSpan(dstBase + (y + 1) * px + 1, nx));
            }
        }

        MeshGeometry3D mesh = VoxelSurfaceMesher.BuildNaiveOuterSurface(
            padded,
            px,
            py,
            pz,
            b.X - gridStepMm,
            b.Y - gridStepMm,
            b.Z - gridStepMm,
            gridStepMm);

        var field = new CompositeSubtractField(stock.Field, swept.Field);
        return BrepFromMesh.Build(mesh, field);
    }

    /// <summary>
    /// Консервативная проверка пересечения ячейки swept-объёмом:
    /// центр + 8 вершин. Это снижает риск пропуска реза при грубой сетке.
    /// </summary>
    private static bool CellIntersectsField(ISolidField field, double x0, double y0, double z0, double h)
    {
        double x1 = x0 + h;
        double y1 = y0 + h;
        double z1 = z0 + h;
        if (field.Contains(new Point3D(x0 + 0.5 * h, y0 + 0.5 * h, z0 + 0.5 * h)))
        {
            return true;
        }

        return field.Contains(new Point3D(x0, y0, z0)) ||
               field.Contains(new Point3D(x1, y0, z0)) ||
               field.Contains(new Point3D(x0, y1, z0)) ||
               field.Contains(new Point3D(x1, y1, z0)) ||
               field.Contains(new Point3D(x0, y0, z1)) ||
               field.Contains(new Point3D(x1, y0, z1)) ||
               field.Contains(new Point3D(x0, y1, z1)) ||
               field.Contains(new Point3D(x1, y1, z1));
    }
}

internal sealed class CompositeSubtractField : ISolidField
{
    private readonly ISolidField _a;
    private readonly ISolidField _b;
    public CompositeSubtractField(ISolidField a, ISolidField b)
    {
        _a = a;
        _b = b;
        Bounds = a.Bounds;
    }

    public Rect3D Bounds { get; }
    public bool Contains(Point3D p) => _a.Contains(p) && !_b.Contains(p);
}
