using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    public sealed class MachineAttachmentSphereVisual : ModelVisual3D
    {
        public const double DefaultBaseRadiusMm = 5;
        public const double HoverScale = 2.0;

        private readonly GeometryModel3D _geometryModel;
        private Color _baseColor;
        private double _baseRadiusMm = DefaultBaseRadiusMm;
        private double _radius = DefaultBaseRadiusMm;

        public MachineAttachmentSphereVisual(string sphereId, Color color, double baseRadiusMm = DefaultBaseRadiusMm)
        {
            SphereId = sphereId;
            _geometryModel = new GeometryModel3D();
            Content = _geometryModel;
            SetBaseRadius(baseRadiusMm);
            SetColor(color);
        }

        public string SphereId { get; }

        public GeometryModel3D PickGeometry => _geometryModel;

        public double CurrentRadiusMm => _radius;

        public void SetCenter(Point3D center) =>
            Transform = new TranslateTransform3D(center.X, center.Y, center.Z);

        public void SetBaseRadius(double baseRadiusMm)
        {
            _baseRadiusMm = Math.Max(baseRadiusMm, 1);
            if (!_isHovered)
            {
                SetRadius(_baseRadiusMm);
            }
        }

        private bool _isHovered;

        public void SetHovered(bool hovered)
        {
            _isHovered = hovered;
            SetRadius(hovered ? _baseRadiusMm * HoverScale : _baseRadiusMm);
        }

        public void SetColor(Color color)
        {
            _baseColor = color;
            ApplyMaterial(color);
        }

        public void RestoreBaseColor() => ApplyMaterial(_baseColor);

        public void SetFlashColor(Color color) => ApplyMaterial(color);

        private void SetRadius(double radiusMm)
        {
            if (Math.Abs(_radius - radiusMm) < 1e-9)
            {
                return;
            }

            _radius = radiusMm;
            var builder = new MeshBuilder(false, false);
            builder.AddSphere(new Point3D(0, 0, 0), radiusMm, 12, 12);
            _geometryModel.Geometry = builder.ToMesh();
        }

        private void ApplyMaterial(Color color)
        {
            Material material = CreateMaterial(color);
            _geometryModel.Material = material;
            _geometryModel.BackMaterial = material;
        }

        private static Material CreateMaterial(Color color)
        {
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            var emissive = new EmissiveMaterial(brush);
            var diffuse = new DiffuseMaterial(brush);
            return new MaterialGroup { Children = { diffuse, emissive } };
        }
    }
}
