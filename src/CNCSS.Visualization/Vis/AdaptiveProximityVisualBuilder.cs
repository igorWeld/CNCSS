using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data.Config;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>
    /// Визуализация зоны адаптивного LOD: цилиндр вдоль −Z от точки крепления,
    /// радиус режущей части × <see cref="SimulationConstants.AdaptiveProximityRadiusFactor"/>.
    /// </summary>
    public static class AdaptiveProximityVisualBuilder
    {
        private const byte FillAlpha = (byte)(255 * 0.25);
        private const int CylinderDivisions = 20;

        /// <summary>Меш цилиндра в tool-local: Z=0 — крепление, Z=−overallLength — кончик.</summary>
        public static MeshGeometry3D BuildMesh(double toolRadiusMm, double overallLengthMm)
        {
            double radius = Math.Max(1e-4, toolRadiusMm * SimulationConstants.AdaptiveProximityRadiusFactor);
            double length = Math.Max(1e-3, overallLengthMm);
            var builder = new MeshBuilder(false, false);
            builder.AddCylinder(
                new Point3D(0, 0, 0),
                new Point3D(0, 0, -length),
                radius,
                CylinderDivisions,
                true,
                true);
            return builder.ToMesh()!;
        }

        /// <summary>Материал 25% непрозрачности (голубой).</summary>
        public static Material CreateMaterial()
        {
            var brush = new SolidColorBrush(Color.FromArgb(FillAlpha, 90, 170, 255));
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            var diffuse = new DiffuseMaterial(brush);
            var emissive = new EmissiveMaterial(brush);
            var material = new MaterialGroup { Children = { diffuse, emissive } };
            if (material.CanFreeze)
            {
                material.Freeze();
            }

            return material;
        }
    }
}
