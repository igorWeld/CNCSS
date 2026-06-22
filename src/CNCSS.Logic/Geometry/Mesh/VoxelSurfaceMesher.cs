using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Geometry.Mesh;

/// <summary>Наивное построение внешней поверхности по маске вокселей (1 = тело).</summary>
public static class VoxelSurfaceMesher
{
    /// <summary>Экструзия граней на границе solid/air.</summary>
    public static MeshGeometry3D BuildNaiveOuterSurface(
        ReadOnlySpan<byte> mat,
        int nx,
        int ny,
        int nz,
        double ox,
        double oy,
        double oz,
        double res)
    {
        int cellCount = nx * ny * nz;
        if (mat.Length < cellCount)
        {
            throw new ArgumentException("Span shorter than nx*ny*nz.", nameof(mat));
        }

        var builder = new MeshBuilder(false, false);
        for (int iz = 0; iz < nz; iz++)
        {
            double z0 = oz + iz * res;
            double z1 = z0 + res;
            for (int iy = 0; iy < ny; iy++)
            {
                double y0 = oy + iy * res;
                double y1 = y0 + res;
                for (int ix = 0; ix < nx; ix++)
                {
                    if (!Solid(mat, nx, ny, nz, ix, iy, iz))
                    {
                        continue;
                    }

                    double x0 = ox + ix * res;
                    double x1 = x0 + res;
                    if (!Solid(mat, nx, ny, nz, ix - 1, iy, iz))
                    {
                        AddQuad(builder, x0, y0, z0, x0, y1, z0, x0, y1, z1, x0, y0, z1);
                    }

                    if (!Solid(mat, nx, ny, nz, ix + 1, iy, iz))
                    {
                        AddQuad(builder, x1, y0, z1, x1, y1, z1, x1, y1, z0, x1, y0, z0);
                    }

                    if (!Solid(mat, nx, ny, nz, ix, iy - 1, iz))
                    {
                        AddQuad(builder, x0, y0, z1, x1, y0, z1, x1, y0, z0, x0, y0, z0);
                    }

                    if (!Solid(mat, nx, ny, nz, ix, iy + 1, iz))
                    {
                        AddQuad(builder, x0, y1, z0, x1, y1, z0, x1, y1, z1, x0, y1, z1);
                    }

                    if (!Solid(mat, nx, ny, nz, ix, iy, iz - 1))
                    {
                        AddQuad(builder, x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0);
                    }

                    if (!Solid(mat, nx, ny, nz, ix, iy, iz + 1))
                    {
                        AddQuad(builder, x0, y1, z1, x1, y1, z1, x1, y0, z1, x0, y0, z1);
                    }
                }
            }
        }

        return builder.ToMesh();
    }

    private static bool Solid(ReadOnlySpan<byte> mat, int nx, int ny, int nz, int ix, int iy, int iz)
    {
        if ((uint)ix >= (uint)nx || (uint)iy >= (uint)ny || (uint)iz >= (uint)nz)
        {
            return false;
        }

        return mat[ix + nx * (iy + ny * iz)] != 0;
    }

    private static void AddQuad(
        MeshBuilder builder,
        double x0, double y0, double z0,
        double x1, double y1, double z1,
        double x2, double y2, double z2,
        double x3, double y3, double z3)
    {
        builder.AddTriangle(new Point3D(x0, y0, z0), new Point3D(x1, y1, z1), new Point3D(x2, y2, z2));
        builder.AddTriangle(new Point3D(x0, y0, z0), new Point3D(x2, y2, z2), new Point3D(x3, y3, z3));
    }
}
