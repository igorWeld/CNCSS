using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CNCSS.Vis
{
    internal static class MeshWireframeBuilder
    {
        public static Point3DCollection? FromModel(Model3D? model)
        {
            if (model == null)
            {
                return null;
            }

            var points = new Point3DCollection();
            AppendWireframePoints(model, points);
            return points.Count == 0 ? null : points;
        }

        private static void AppendWireframePoints(Model3D model, Point3DCollection points)
        {
            if (model is Model3DGroup group)
            {
                foreach (Model3D child in group.Children)
                {
                    AppendWireframePoints(child, points);
                }

                return;
            }

            if (model is not GeometryModel3D geom || geom.Geometry is not MeshGeometry3D mesh)
            {
                return;
            }

            if (mesh.Positions == null || mesh.TriangleIndices == null || mesh.TriangleIndices.Count < 3)
            {
                return;
            }

            var edges = new HashSet<(int a, int b)>();
            Int32Collection ti = mesh.TriangleIndices;
            for (int i = 0; i + 2 < ti.Count; i += 3)
            {
                AddEdge(edges, ti[i], ti[i + 1]);
                AddEdge(edges, ti[i + 1], ti[i + 2]);
                AddEdge(edges, ti[i + 2], ti[i]);
            }

            Point3DCollection pos = mesh.Positions;
            foreach ((int a, int b) in edges)
            {
                if ((uint)a >= (uint)pos.Count || (uint)b >= (uint)pos.Count)
                {
                    continue;
                }

                points.Add(pos[a]);
                points.Add(pos[b]);
            }
        }

        private static void AddEdge(HashSet<(int a, int b)> edges, int i0, int i1)
        {
            if (i0 == i1)
            {
                return;
            }

            int a = i0 < i1 ? i0 : i1;
            int b = i0 < i1 ? i1 : i0;
            edges.Add((a, b));
        }
    }
}
