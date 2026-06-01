using System;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Совместное наивное экструзионное мешинга по маске вокселей (1 = тело).</summary>
    public static class VoxelSurfaceMesher
    {
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

            static bool Solid(ReadOnlySpan<byte> m, int sx, int sy, int sz, int ix, int iy, int iz)
            {
                if ((uint)ix >= (uint)sx || (uint)iy >= (uint)sy || (uint)iz >= (uint)sz)
                {
                    return false;
                }

                return m[ix + sx * (iy + sy * iz)] != 0;
            }

            var mb = new MeshBuilder(false, false);

            void Quad(Point3D a, Point3D b, Point3D c, Point3D d)
            {
                mb.AddTriangle(a, b, c);
                mb.AddTriangle(a, c, d);
            }

            for (int iz = 0; iz < nz; iz++)
            {
                for (int iy = 0; iy < ny; iy++)
                {
                    for (int ix = 0; ix < nx; ix++)
                    {
                        if (!Solid(mat, nx, ny, nz, ix, iy, iz))
                        {
                            continue;
                        }

                        double x0 = ox + ix * res;
                        double x1 = x0 + res;
                        double y0 = oy + iy * res;
                        double y1 = y0 + res;
                        double z0 = oz + iz * res;
                        double z1 = z0 + res;

                        if (!Solid(mat, nx, ny, nz, ix - 1, iy, iz))
                        {
                            Quad(
                                new Point3D(x0, y0, z0),
                                new Point3D(x0, y1, z0),
                                new Point3D(x0, y1, z1),
                                new Point3D(x0, y0, z1));
                        }

                        if (!Solid(mat, nx, ny, nz, ix + 1, iy, iz))
                        {
                            Quad(
                                new Point3D(x1, y0, z0),
                                new Point3D(x1, y0, z1),
                                new Point3D(x1, y1, z1),
                                new Point3D(x1, y1, z0));
                        }

                        if (!Solid(mat, nx, ny, nz, ix, iy - 1, iz))
                        {
                            Quad(
                                new Point3D(x0, y0, z0),
                                new Point3D(x0, y0, z1),
                                new Point3D(x1, y0, z1),
                                new Point3D(x1, y0, z0));
                        }

                        if (!Solid(mat, nx, ny, nz, ix, iy + 1, iz))
                        {
                            Quad(
                                new Point3D(x0, y1, z0),
                                new Point3D(x1, y1, z0),
                                new Point3D(x1, y1, z1),
                                new Point3D(x0, y1, z1));
                        }

                        if (!Solid(mat, nx, ny, nz, ix, iy, iz - 1))
                        {
                            Quad(
                                new Point3D(x0, y0, z0),
                                new Point3D(x1, y0, z0),
                                new Point3D(x1, y1, z0),
                                new Point3D(x0, y1, z0));
                        }

                        if (!Solid(mat, nx, ny, nz, ix, iy, iz + 1))
                        {
                            Quad(
                                new Point3D(x0, y0, z1),
                                new Point3D(x0, y1, z1),
                                new Point3D(x1, y1, z1),
                                new Point3D(x1, y0, z1));
                        }
                    }
                }
            }

            MeshGeometry3D? mesh = mb.ToMesh();
            return mesh ?? new MeshGeometry3D();
        }
    }
}
