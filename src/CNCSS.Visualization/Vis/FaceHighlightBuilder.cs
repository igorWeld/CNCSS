using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    public static class FaceHighlightBuilder
    {
        public static ModelVisual3D BuildMarker(
            MeshFacePick pick,
            Color color,
            Transform3D? meshToWorld = null,
            double fallbackSizeMm = 14)
        {
            if (pick.SourceMesh != null
                && pick.SeedTriangleIndex >= 0
                && meshToWorld != null)
            {
                return BuildSurfaceMarker(pick.SourceMesh, pick.SeedTriangleIndex, meshToWorld, color);
            }

            return BuildPointMarker(pick, color, fallbackSizeMm);
        }

        private static ModelVisual3D BuildSurfaceMarker(
            MeshGeometry3D sourceMesh,
            int seedTriangleIndex,
            Transform3D meshToWorld,
            Color color)
        {
            MeshGeometry3D patch = MeshFaceRaycaster.BuildCoplanarPatchWorld(sourceMesh, seedTriangleIndex, meshToWorld);
            var highlightColor = Color.FromArgb(140, color.R, color.G, color.B);
            var material = MaterialHelper.CreateMaterial(highlightColor);
            var geom = new GeometryModel3D
            {
                Geometry = patch,
                Material = material,
                BackMaterial = material
            };

            return new ModelVisual3D { Content = geom };
        }

        private static ModelVisual3D BuildPointMarker(MeshFacePick pick, Color color, double sizeMm)
        {
            Vector3D normal = pick.NormalWorld;
            if (normal.LengthSquared < 1e-12)
            {
                normal = new Vector3D(0, 0, 1);
            }
            else
            {
                normal.Normalize();
            }

            Vector3D tangent = Math.Abs(normal.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            Vector3D u = Vector3D.CrossProduct(normal, tangent);
            u.Normalize();
            Vector3D v = Vector3D.CrossProduct(normal, u);
            v.Normalize();

            double half = sizeMm * 0.5;
            Point3D center = pick.PointWorld;
            Point3D p0 = center + (-u * half) + (-v * half);
            Point3D p1 = center + (u * half) + (-v * half);
            Point3D p2 = center + (u * half) + (v * half);
            Point3D p3 = center + (-u * half) + (v * half);

            var builder = new MeshBuilder(false, false);
            builder.AddTriangle(p0, p1, p2);
            builder.AddTriangle(p0, p2, p3);

            var material = MaterialHelper.CreateMaterial(Color.FromArgb(190, color.R, color.G, color.B));
            var geom = new GeometryModel3D
            {
                Geometry = builder.ToMesh(),
                Material = material,
                BackMaterial = material
            };

            return new ModelVisual3D { Content = geom };
        }
    }
}
