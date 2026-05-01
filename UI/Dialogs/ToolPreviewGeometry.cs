using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.UI.Dialogs
{
    /// <summary>Строит упрощённую 3D-модель концевой фрезы (режущая часть + державка) для окна параметров инструмента.</summary>
    public static class ToolPreviewGeometry
    {
        public static Model3DGroup BuildCylinderTool(
            double fluteRadius,
            double shankRadius,
            double fluteLength,
            double overallLength,
            Color fluteColor,
            Color shankColor)
        {
            fluteLength = Math.Max(1e-3, fluteLength);
            overallLength = Math.Max(fluteLength, overallLength);

            var group = new Model3DGroup();

            var fluteBuilder = new MeshBuilder();
            fluteBuilder.AddCylinder(
                new Point3D(0, 0, 0),
                new Point3D(0, 0, fluteLength),
                fluteRadius,
                20,
                true,
                true);

            Material fluteMat = MaterialHelper.CreateMaterial(fluteColor);
            var fluteGm = new GeometryModel3D(fluteBuilder.ToMesh(), fluteMat)
            {
                BackMaterial = fluteMat
            };
            group.Children.Add(fluteGm);

            var shankBuilder = new MeshBuilder();
            shankBuilder.AddCylinder(
                new Point3D(0, 0, fluteLength),
                new Point3D(0, 0, overallLength),
                shankRadius,
                16,
                true,
                true);

            Material shMat = MaterialHelper.CreateMaterial(shankColor);
            var shankGm = new GeometryModel3D(shankBuilder.ToMesh(), shMat)
            {
                BackMaterial = shMat
            };
            group.Children.Add(shankGm);

            return group;
        }
    }
}
