using System.IO;
using System.Text;
using System.Windows.Media.Media3D;

namespace CNCSS.Vis
{
    /// <summary>Exports WPF mesh geometry to binary STL (one solid per file).</summary>
    public static class BinaryStlExporter
    {
        public static void Write(string path, Model3D model)
        {
            var triangles = new List<StlTriangle>();
            CollectTriangles(model, Transform3D.Identity, triangles);
            if (triangles.Count == 0)
            {
                throw new InvalidOperationException("Модель не содержит треугольников для экспорта в STL.");
            }

            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
            writer.Write(new byte[80]);
            writer.Write((uint)triangles.Count);
            foreach (StlTriangle triangle in triangles)
            {
                WriteVector(writer, triangle.Normal);
                WriteVector(writer, triangle.V0);
                WriteVector(writer, triangle.V1);
                WriteVector(writer, triangle.V2);
                writer.Write((ushort)0);
            }
        }

        private static void CollectTriangles(Model3D model, Transform3D parentTransform, List<StlTriangle> output)
        {
            if (model is GeometryModel3D geom && geom.Geometry is MeshGeometry3D mesh)
            {
                Transform3D transform = Combine(parentTransform, geom.Transform);
                AddMeshTriangles(mesh, transform, output);
                return;
            }

            if (model is Model3DGroup group)
            {
                Transform3D transform = Combine(parentTransform, group.Transform);
                foreach (Model3D child in group.Children)
                {
                    CollectTriangles(child, transform, output);
                }
            }
        }

        private static void AddMeshTriangles(MeshGeometry3D mesh, Transform3D transform, List<StlTriangle> output)
        {
            if (mesh.Positions == null || mesh.TriangleIndices == null)
            {
                return;
            }

            for (int i = 0; i + 2 < mesh.TriangleIndices.Count; i += 3)
            {
                Point3D p0 = transform.Transform(mesh.Positions[mesh.TriangleIndices[i]]);
                Point3D p1 = transform.Transform(mesh.Positions[mesh.TriangleIndices[i + 1]]);
                Point3D p2 = transform.Transform(mesh.Positions[mesh.TriangleIndices[i + 2]]);
                Vector3D normal = Vector3D.CrossProduct(p1 - p0, p2 - p0);
                if (normal.LengthSquared > 1e-12)
                {
                    normal.Normalize();
                }
                else
                {
                    normal = new Vector3D(0, 0, 1);
                }

                output.Add(new StlTriangle(normal, p0, p1, p2));
            }
        }

        private static Transform3D Combine(Transform3D parent, Transform3D? local)
        {
            if (local == null || local == Transform3D.Identity)
            {
                return parent;
            }

            if (parent == Transform3D.Identity)
            {
                return local;
            }

            return new MatrixTransform3D(parent.Value * local.Value);
        }

        private static void WriteVector(BinaryWriter writer, Vector3D vector)
        {
            writer.Write((float)vector.X);
            writer.Write((float)vector.Y);
            writer.Write((float)vector.Z);
        }

        private static void WriteVector(BinaryWriter writer, Point3D point) =>
            WriteVector(writer, new Vector3D(point.X, point.Y, point.Z));

        private readonly record struct StlTriangle(Vector3D Normal, Point3D V0, Point3D V1, Point3D V2);
    }
}
