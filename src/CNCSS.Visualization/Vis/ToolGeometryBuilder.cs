using System.Windows.Media.Media3D;
using CNCSS.Data.Tools;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Tool mesh in holder-local space: Z=0 at collet/shank top, tip at Z=-overallLength.</summary>
    public static class ToolGeometryBuilder
    {
        public static (MeshGeometry3D Flute, MeshGeometry3D Shank) Build(
            ToolType type,
            double diameter,
            double shankDiameter,
            double fluteLength,
            double overallLength,
            double pointAngleDegrees)
        {
            overallLength = Math.Max(fluteLength, overallLength);
            double fluteRadius = Math.Max(1e-4, diameter * 0.5);
            double shankRadius = Math.Max(1e-4, shankDiameter * 0.5);
            double shankLen = Math.Max(0, overallLength - fluteLength);
            double tipZ = -overallLength;
            double fluteTopZ = tipZ + fluteLength;

            MeshGeometry3D fluteGeom = type == ToolType.Drill
                ? BuildDrillFluteDown(fluteRadius, fluteLength, pointAngleDegrees, tipZ)
                : BuildCylinderMesh(fluteTopZ, tipZ, fluteRadius);

            MeshGeometry3D shankGeom = shankLen > 1e-6
                ? BuildCylinderMesh(0, -shankLen, shankRadius)
                : new MeshGeometry3D();

            return (fluteGeom, shankGeom);
        }

        private static MeshGeometry3D BuildCylinderMesh(double z0, double z1, double radius)
        {
            var builder = new MeshBuilder();
            builder.AddCylinder(new Point3D(0, 0, z0), new Point3D(0, 0, z1), radius, 20, true, true);
            return builder.ToMesh()!;
        }

        private static MeshGeometry3D BuildDrillFluteDown(double fluteRadius, double fluteLength, double pointAngleDegrees, double tipZ)
        {
            fluteRadius = Math.Max(1e-4, fluteRadius);
            fluteLength = Math.Max(1e-3, fluteLength);

            double halfAngleDeg = Math.Clamp(pointAngleDegrees, 1.0, 179.0) * 0.5;
            double halfRad = halfAngleDeg * (Math.PI / 180.0);
            double tanHalf = Math.Tan(halfRad);
            if (tanHalf < 1e-10)
            {
                tanHalf = 1e-10;
            }

            double hFullCone = fluteRadius / tanHalf;
            var mb = new MeshBuilder();
            const int thetaCone = 24;
            var apex = new Point3D(0, 0, tipZ);

            if (fluteLength <= hFullCone + 1e-9)
            {
                double rBase = fluteRadius * (fluteLength / hFullCone);
                mb.AddCone(new Point3D(0, 0, tipZ + fluteLength), apex, rBase, false, thetaCone);
            }
            else
            {
                double coneBaseZ = tipZ + hFullCone;
                mb.AddCone(new Point3D(0, 0, coneBaseZ), apex, fluteRadius, false, thetaCone);
                mb.AddCylinder(new Point3D(0, 0, coneBaseZ), new Point3D(0, 0, tipZ + fluteLength), fluteRadius, 20, true, true);
            }

            return mb.ToMesh()!;
        }
    }
}
