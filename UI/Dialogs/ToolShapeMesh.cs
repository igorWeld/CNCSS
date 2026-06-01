using System;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.UI.Dialogs
{
    /// <summary>Совместное построение мешей режущей части для превью и основного вида станка.</summary>
    public static class ToolShapeMesh
    {
        /// <summary>Конический наконечник + цилиндр: вершина в (0,0,0), ось +Z как у текущего цилиндра концевых фрез.</summary>
        public static MeshGeometry3D BuildDrillCuttingMesh(double fluteRadius, double fluteLength, double pointAngleDegrees)
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
            var apex = new Point3D(0, 0, 0);

            if (fluteLength <= hFullCone + 1e-9)
            {
                double rBase = fluteRadius * (fluteLength / hFullCone);
                mb.AddCone(new Point3D(0, 0, fluteLength), apex, rBase, false, thetaCone);
            }
            else
            {
                mb.AddCone(new Point3D(0, 0, hFullCone), apex, fluteRadius, false, thetaCone);
                mb.AddCylinder(new Point3D(0, 0, hFullCone), new Point3D(0, 0, fluteLength), fluteRadius, 20, true, true);
            }

            return mb.ToMesh()!;
        }
    }
}
