using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>
    /// Маркер нуля WCS: полупрозрачная белая сфера, RGB-оси, подпись G54…G59.
    /// </summary>
    public static class WcsMarkerVisualBuilder
    {
        private const int ShaftDivisions = 10;

        private const byte MarkerAlpha = 255;

        /// <summary>90% прозрачности — сфера не перекрывает заготовку/станок.</summary>
        private const byte SphereAlpha = (byte)(255 * 0.1);

        private const double AxisThicknessScale = 0.8;

        private static readonly Color LabelColor = Color.FromRgb(255, 240, 245);

        private const double LabelFontSize = 15 * 1.7 * 0.65;

        /// <summary>Толщина обводки контура букв — доля от размера шрифта.</summary>
        private const double LabelOutlineThicknessFactor = 0.01;

        public static ModelVisual3D Build(string coordinateSystemLabel, double sphereRadiusMm, double axisLenMm)
        {
            var root = new ModelVisual3D();
            double axisLen = Math.Max(axisLenMm, 8);
            double shaftRadius = Math.Clamp(axisLen * 0.05 * AxisThicknessScale, 0.55, 1.1);
            double tipRadius = shaftRadius * 1.75;
            double shaftLength = axisLen * 0.78;

            AddAxisArrow(root, new Vector3D(1, 0, 0), Colors.Red, sphereRadiusMm, shaftLength, shaftRadius, tipRadius, useSphereTip: false);
            AddAxisArrow(root, new Vector3D(0, 1, 0), Colors.Green, sphereRadiusMm, shaftLength, shaftRadius, tipRadius, useSphereTip: false);
            AddAxisArrow(root, new Vector3D(0, 0, 1), Colors.Blue, sphereRadiusMm, shaftLength, shaftRadius, tipRadius, useSphereTip: true);

            AddOriginSphere(root, sphereRadiusMm);

            AddCoordinateLabel(root, coordinateSystemLabel, axisLen, sphereRadiusMm);
            return root;
        }

        private static void AddOriginSphere(ModelVisual3D root, double radiusMm)
        {
            var builder = new MeshBuilder(false, false);
            builder.AddSphere(new Point3D(0, 0, 0), radiusMm, 14, 14);
            root.Children.Add(CreateMeshVisual(builder, CreateTransparentWhiteSphereMaterial()));
        }

        private static void AddAxisArrow(
            ModelVisual3D root,
            Vector3D direction,
            Color color,
            double sphereRadiusMm,
            double shaftLength,
            double shaftRadius,
            double tipRadius,
            bool useSphereTip)
        {
            direction.Normalize();
            var shaftStart = new Point3D(
                direction.X * sphereRadiusMm,
                direction.Y * sphereRadiusMm,
                direction.Z * sphereRadiusMm);
            var shaftEnd = new Point3D(
                direction.X * shaftLength,
                direction.Y * shaftLength,
                direction.Z * shaftLength);

            Material material = CreateFlatEmissiveMaterial(color);

            var shaftBuilder = new MeshBuilder(false, false);
            shaftBuilder.AddCylinder(shaftStart, shaftEnd, shaftRadius, ShaftDivisions, true, false);
            root.Children.Add(CreateMeshVisual(shaftBuilder, material));

            if (useSphereTip)
            {
                var sphereBuilder = new MeshBuilder(false, false);
                sphereBuilder.AddSphere(shaftEnd, tipRadius, ShaftDivisions, ShaftDivisions);
                root.Children.Add(CreateMeshVisual(sphereBuilder, material));
            }
            else
            {
                var tipEnd = new Point3D(
                    direction.X * (shaftLength + tipRadius * 2.4),
                    direction.Y * (shaftLength + tipRadius * 2.4),
                    direction.Z * (shaftLength + tipRadius * 2.4));
                var tipBuilder = new MeshBuilder(false, false);
                tipBuilder.AddCone(shaftEnd, tipEnd, tipRadius, false, ShaftDivisions);
                root.Children.Add(CreateMeshVisual(tipBuilder, material));
            }
        }

        private static ModelVisual3D CreateMeshVisual(MeshBuilder builder, Material material)
        {
            var geometry = new GeometryModel3D
            {
                Geometry = builder.ToMesh(),
                Material = material,
                BackMaterial = material
            };
            return new ModelVisual3D { Content = geometry };
        }

        private static void AddCoordinateLabel(ModelVisual3D root, string label, double axisLenMm, double sphereRadiusMm)
        {
            root.Children.Add(CreateOutlinedLabelBillboard(
                label,
                new Point3D(axisLenMm * 0.42, -axisLenMm * 0.08, sphereRadiusMm * 2.2)));
        }

        private static BillboardVisual3D CreateOutlinedLabelBillboard(string text, Point3D position)
        {
            double outlineThickness = LabelFontSize * LabelOutlineThicknessFactor;
            RenderTargetBitmap bitmap = RenderOutlinedTextBitmap(
                text,
                LabelFontSize,
                LabelColor,
                Colors.Black,
                outlineThickness,
                out double pixelWidth,
                out double pixelHeight);

            var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            var material = new EmissiveMaterial(brush);
            if (material.CanFreeze)
            {
                material.Freeze();
            }

            return new BillboardVisual3D
            {
                Position = position,
                Width = pixelWidth,
                Height = pixelHeight,
                Material = material
            };
        }

        private static RenderTargetBitmap RenderOutlinedTextBitmap(
            string text,
            double fontSize,
            Color fillColor,
            Color outlineColor,
            double outlineThickness,
            out double pixelWidth,
            out double pixelHeight)
        {
            var typeface = new Typeface(
                new FontFamily("Segoe UI"),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);

            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                pixelsPerDip: 1.0);

            double pad = outlineThickness;

            System.Windows.Media.Geometry textGeometry = formattedText.BuildGeometry(new Point(pad, pad));
            textGeometry.Freeze();

            pixelWidth = formattedText.Width + pad * 2;
            pixelHeight = formattedText.Height + pad * 2;

            var outlinePen = new Pen(new SolidColorBrush(outlineColor), outlineThickness)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            if (outlinePen.CanFreeze)
            {
                outlinePen.Freeze();
            }

            var fillBrush = new SolidColorBrush(fillColor);
            if (fillBrush.CanFreeze)
            {
                fillBrush.Freeze();
            }

            var drawingVisual = new DrawingVisual();
            using (DrawingContext drawingContext = drawingVisual.RenderOpen())
            {
                drawingContext.DrawGeometry(null, outlinePen, textGeometry);
                drawingContext.DrawGeometry(fillBrush, null, textGeometry);
            }

            int widthPx = Math.Max(1, (int)Math.Ceiling(pixelWidth));
            int heightPx = Math.Max(1, (int)Math.Ceiling(pixelHeight));
            var bitmap = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawingVisual);
            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            return bitmap;
        }

        private static Material CreateTransparentWhiteSphereMaterial()
        {
            var brush = new SolidColorBrush(Color.FromArgb(SphereAlpha, 255, 255, 255));
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

        private static Material CreateFlatEmissiveMaterial(Color color)
        {
            var brush = new SolidColorBrush(ApplyMarkerAlpha(color));
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            var material = new EmissiveMaterial(brush);
            if (material.CanFreeze)
            {
                material.Freeze();
            }

            return material;
        }

        private static Color ApplyMarkerAlpha(Color color) =>
            Color.FromArgb(MarkerAlpha, color.R, color.G, color.B);
    }
}
