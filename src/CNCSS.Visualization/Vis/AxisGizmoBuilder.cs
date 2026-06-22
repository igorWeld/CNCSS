using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    public enum GizmoAxis
    {
        X = 0,
        Y = 1,
        Z = 2
    }

    public sealed class AxisGizmoBuildResult
    {
        public required ModelVisual3D Root { get; init; }
        public required IReadOnlyList<(GeometryModel3D Geometry, GizmoAxis Axis)> HitGeometries { get; init; }
        public required Point3D MeshCenterLocal { get; init; }
    }

    /// <summary>RGB axis arrows centered on mesh bbox for drag-editing offsets.</summary>
    public static class AxisGizmoBuilder
    {
        private const int ShaftDivisions = 8;

        /// <summary>Total arrow length (mm), longer than the part bbox for visibility.</summary>
        public static double ComputeArrowLengthMm(double meshExtentMm)
        {
            double extent = Math.Max(meshExtentMm, 1);
            return Math.Clamp(extent * 1.45, 120, 4000);
        }

        public static AxisGizmoBuildResult Build(Point3D meshCenterLocal, double targetExtentMm)
        {
            double length = ComputeArrowLengthMm(targetExtentMm);
            double shaftRadius = Math.Max(3.5, length * 0.028);
            double tipRadius = shaftRadius * 1.85;
            double shaftLength = length * 0.82;

            var root = new ModelVisual3D
            {
                Transform = new TranslateTransform3D(meshCenterLocal.X, meshCenterLocal.Y, meshCenterLocal.Z)
            };

            var hitGeometries = new List<(GeometryModel3D, GizmoAxis)>();
            // Standard axis colors: X red, Y green, Z blue.
            AddAxisArrow(root, hitGeometries, GizmoAxis.X, Colors.Red, shaftLength, shaftRadius, tipRadius);
            AddAxisArrow(root, hitGeometries, GizmoAxis.Y, Colors.Green, shaftLength, shaftRadius, tipRadius);
            AddAxisArrow(root, hitGeometries, GizmoAxis.Z, Colors.Blue, shaftLength, shaftRadius, tipRadius);
            AddCenterMarker(root, Math.Max(3.0, shaftRadius * 1.2));

            return new AxisGizmoBuildResult
            {
                Root = root,
                HitGeometries = hitGeometries,
                MeshCenterLocal = meshCenterLocal
            };
        }

        private static void AddCenterMarker(ModelVisual3D root, double radius)
        {
            var builder = new MeshBuilder(false, false);
            builder.AddSphere(new Point3D(0, 0, 0), radius, ShaftDivisions, ShaftDivisions);
            var geom = new GeometryModel3D
            {
                Geometry = builder.ToMesh(),
                Material = MaterialHelper.CreateMaterial(Color.FromArgb(200, 255, 255, 255)),
                BackMaterial = MaterialHelper.CreateMaterial(Color.FromArgb(200, 220, 220, 220))
            };
            root.Children.Add(new ModelVisual3D { Content = geom });
        }

        private static void AddAxisArrow(
            ModelVisual3D root,
            List<(GeometryModel3D, GizmoAxis)> hits,
            GizmoAxis axis,
            Color color,
            double shaftLength,
            double shaftRadius,
            double tipRadius)
        {
            Vector3D direction = axis switch
            {
                GizmoAxis.X => new Vector3D(1, 0, 0),
                GizmoAxis.Y => new Vector3D(0, 1, 0),
                _ => new Vector3D(0, 0, 1)
            };

            Point3D shaftEnd = new(direction.X * shaftLength, direction.Y * shaftLength, direction.Z * shaftLength);
            Point3D tipEnd = new(direction.X * (shaftLength + tipRadius * 2.2), direction.Y * (shaftLength + tipRadius * 2.2), direction.Z * (shaftLength + tipRadius * 2.2));

            var shaftBuilder = new MeshBuilder(false, false);
            shaftBuilder.AddCylinder(new Point3D(0, 0, 0), shaftEnd, shaftRadius, ShaftDivisions, true, false);
            var shaftGeom = CreateHitGeometry(shaftBuilder, color);
            hits.Add((shaftGeom, axis));
            root.Children.Add(new ModelVisual3D { Content = shaftGeom });

            var tipBuilder = new MeshBuilder(false, false);
            tipBuilder.AddCone(shaftEnd, tipEnd, tipRadius, false, ShaftDivisions);
            var tipGeom = CreateHitGeometry(tipBuilder, color);
            hits.Add((tipGeom, axis));
            root.Children.Add(new ModelVisual3D { Content = tipGeom });
        }

        public static AxisGizmoBuildResult BuildOverlay(Point3D meshCenterLocal, double targetExtentMm)
        {
            AxisGizmoBuildResult result = Build(meshCenterLocal, targetExtentMm);
            ApplyOverlayMaterials(result.Root);
            return result;
        }

        /// <summary>Overlay gizmo with an explicit total arrow length (mm).</summary>
        public static AxisGizmoBuildResult BuildOverlayWithLength(Point3D meshCenterLocal, double arrowLengthMm)
        {
            AxisGizmoBuildResult result = BuildWithLength(meshCenterLocal, arrowLengthMm);
            ApplyOverlayMaterials(result.Root);
            return result;
        }

        /// <summary>
        /// RGB line axes for drag overlay (same style as WCS markers: X red, Y green, Z blue).
        /// </summary>
        public static ModelVisual3D BuildLineOverlay(Point3D meshCenterLocal, double axisLengthMm, double thickness = 3.0)
        {
            var root = new ModelVisual3D
            {
                Transform = new TranslateTransform3D(meshCenterLocal.X, meshCenterLocal.Y, meshCenterLocal.Z)
            };
            AppendLineAxes(root, axisLengthMm, thickness);
            return root;
        }

        /// <summary>Adds X/Y/Z RGB lines at local origin into an existing visual.</summary>
        public static void AppendLineAxes(ModelVisual3D target, double axisLengthMm, double thickness = 3.0)
        {
            double len = Math.Max(axisLengthMm, 1);
            double lineThickness = Math.Clamp(thickness, 1.5, 6.0);
            AddAxisLine(target, Colors.Red, len, GizmoAxis.X, lineThickness);
            AddAxisLine(target, Colors.Green, len, GizmoAxis.Y, lineThickness);
            AddAxisLine(target, Colors.Blue, len, GizmoAxis.Z, lineThickness);
        }

        private static void AddAxisLine(ModelVisual3D root, Color color, double lengthMm, GizmoAxis axis, double thickness)
        {
            Point3D end = axis switch
            {
                GizmoAxis.X => new Point3D(lengthMm, 0, 0),
                GizmoAxis.Y => new Point3D(0, lengthMm, 0),
                _ => new Point3D(0, 0, lengthMm)
            };

            root.Children.Add(new LinesVisual3D
            {
                Color = color,
                Thickness = thickness,
                Points = new Point3DCollection(new[] { new Point3D(0, 0, 0), end })
            });
        }

        public static AxisGizmoBuildResult BuildWithLength(Point3D meshCenterLocal, double arrowLengthMm)
        {
            double length = Math.Max(arrowLengthMm, 1);
            double shaftRadius = Math.Max(2.0, length * 0.028);
            double tipRadius = shaftRadius * 1.85;
            double shaftLength = length * 0.82;

            var root = new ModelVisual3D
            {
                Transform = new TranslateTransform3D(meshCenterLocal.X, meshCenterLocal.Y, meshCenterLocal.Z)
            };

            var hitGeometries = new List<(GeometryModel3D, GizmoAxis)>();
            AddAxisArrow(root, hitGeometries, GizmoAxis.X, Colors.Red, shaftLength, shaftRadius, tipRadius);
            AddAxisArrow(root, hitGeometries, GizmoAxis.Y, Colors.Green, shaftLength, shaftRadius, tipRadius);
            AddAxisArrow(root, hitGeometries, GizmoAxis.Z, Colors.Blue, shaftLength, shaftRadius, tipRadius);
            AddCenterMarker(root, Math.Max(2.0, shaftRadius * 1.0));

            return new AxisGizmoBuildResult
            {
                Root = root,
                HitGeometries = hitGeometries,
                MeshCenterLocal = meshCenterLocal
            };
        }

        private static void ApplyOverlayMaterials(ModelVisual3D root)
        {
            void Apply(Model3D model)
            {
                if (model is GeometryModel3D geom)
                {
                    Color baseColor = Colors.White;
                    if (geom.Material is DiffuseMaterial diffuse && diffuse.Brush is SolidColorBrush sourceBrush)
                    {
                        baseColor = sourceBrush.Color;
                    }
                    else if (geom.Material is EmissiveMaterial emissive && emissive.Brush is SolidColorBrush emissiveBrush)
                    {
                        baseColor = emissiveBrush.Color;
                    }

                    var mat = MaterialHelper.CreateMaterial(Color.FromArgb(255, baseColor.R, baseColor.G, baseColor.B));
                    geom.Material = mat;
                    geom.BackMaterial = mat;
                }
                else if (model is Model3DGroup group)
                {
                    foreach (Model3D child in group.Children)
                    {
                        Apply(child);
                    }
                }
            }

            if (root.Content != null)
            {
                Apply(root.Content);
            }

            foreach (ModelVisual3D child in root.Children)
            {
                if (child.Content != null)
                {
                    Apply(child.Content);
                }
            }
        }

        private static GeometryModel3D CreateHitGeometry(MeshBuilder builder, Color color)
        {
            var mat = MaterialHelper.CreateMaterial(color);
            return new GeometryModel3D
            {
                Geometry = builder.ToMesh(),
                Material = mat,
                BackMaterial = mat
            };
        }
    }
}
