using System.Windows.Media.Media3D;

namespace CNCSS.Machine.Model
{
    /// <summary>Aligns a stock box to a table mounting point.</summary>
    public static class WorkpiecePlacement
    {
        public readonly record struct StockBounds(double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ);

        /// <summary>Stock box in table mesh-local coords; bottom on mounting plane.</summary>
        public static StockBounds AlignToMountTableLocal(
            MachineGeometryPoint mountLocal,
            double width,
            double depth,
            double height,
            double fixtureHeightMm)
        {
            double bottomZ = mountLocal.Z + fixtureHeightMm;
            double halfW = width * 0.5;
            double halfD = depth * 0.5;
            return new StockBounds(
                mountLocal.X - halfW,
                mountLocal.X + halfW,
                mountLocal.Y - halfD,
                mountLocal.Y + halfD,
                bottomZ,
                bottomZ + height);
        }

        public static StockBounds TransformBounds(Matrix3D matrix, StockBounds bounds)
        {
            var corners = new[]
            {
                new Point3D(bounds.MinX, bounds.MinY, bounds.MinZ),
                new Point3D(bounds.MaxX, bounds.MinY, bounds.MinZ),
                new Point3D(bounds.MinX, bounds.MaxY, bounds.MinZ),
                new Point3D(bounds.MaxX, bounds.MaxY, bounds.MinZ),
                new Point3D(bounds.MinX, bounds.MinY, bounds.MaxZ),
                new Point3D(bounds.MaxX, bounds.MinY, bounds.MaxZ),
                new Point3D(bounds.MinX, bounds.MaxY, bounds.MaxZ),
                new Point3D(bounds.MaxX, bounds.MaxY, bounds.MaxZ)
            };

            Point3D first = matrix.Transform(corners[0]);
            double minX = first.X, maxX = first.X;
            double minY = first.Y, maxY = first.Y;
            double minZ = first.Z, maxZ = first.Z;
            for (int i = 1; i < corners.Length; i++)
            {
                Point3D p = matrix.Transform(corners[i]);
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
                minZ = Math.Min(minZ, p.Z);
                maxZ = Math.Max(maxZ, p.Z);
            }

            return new StockBounds(minX, maxX, minY, maxY, minZ, maxZ);
        }

        /// <summary>
        /// Centers stock in XY on the mount point; bottom face at mount Z + fixture height.
        /// </summary>
        public static StockBounds AlignToMount(StockBounds stock, double mountWorldX, double mountWorldY, double mountWorldZ, double fixtureHeightMm)
        {
            double width = stock.MaxX - stock.MinX;
            double depth = stock.MaxY - stock.MinY;
            double height = stock.MaxZ - stock.MinZ;
            double bottomZ = mountWorldZ + fixtureHeightMm;
            double halfW = width * 0.5;
            double halfD = depth * 0.5;
            return new StockBounds(
                mountWorldX - halfW,
                mountWorldX + halfW,
                mountWorldY - halfD,
                mountWorldY + halfD,
                bottomZ,
                bottomZ + height);
        }
    }
}
