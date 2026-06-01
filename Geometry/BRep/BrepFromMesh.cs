using System.Windows.Media.Media3D;

namespace CNCSS.Geometry.BRep;

public static class BrepFromMesh
{
    public static BrepSolid Build(MeshGeometry3D mesh, ISolidField field)
    {
        var verts = new List<BrepVertex>(mesh.Positions.Count);
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            verts.Add(new BrepVertex { Id = i, Position = mesh.Positions[i] });
        }

        var halfEdges = new List<BrepHalfEdge>(mesh.TriangleIndices.Count);
        var faces = new List<BrepFace>(mesh.TriangleIndices.Count / 3);
        var edgeMap = new Dictionary<(int a, int b), BrepHalfEdge>();
        int heId = 0;

        for (int t = 0; t + 2 < mesh.TriangleIndices.Count; t += 3)
        {
            int a = mesh.TriangleIndices[t];
            int b = mesh.TriangleIndices[t + 1];
            int c = mesh.TriangleIndices[t + 2];
            var face = new BrepFace { Id = faces.Count };

            var e0 = new BrepHalfEdge { Id = heId++, Start = verts[a], Face = face };
            var e1 = new BrepHalfEdge { Id = heId++, Start = verts[b], Face = face };
            var e2 = new BrepHalfEdge { Id = heId++, Start = verts[c], Face = face };
            e0.Next = e1;
            e1.Next = e2;
            e2.Next = e0;
            face.Outer.First = e0;

            Vector3D n = Vector3D.CrossProduct(verts[b].Position - verts[a].Position, verts[c].Position - verts[a].Position);
            if (n.Length > 1e-12)
            {
                n.Normalize();
            }

            face.Normal = n;
            faces.Add(face);
            halfEdges.Add(e0);
            halfEdges.Add(e1);
            halfEdges.Add(e2);
            LinkTwin(edgeMap, e0, a, b);
            LinkTwin(edgeMap, e1, b, c);
            LinkTwin(edgeMap, e2, c, a);
        }

        return new BrepSolid
        {
            Vertices = verts,
            HalfEdges = halfEdges,
            Faces = faces,
            Field = field,
            Mesh = mesh
        };
    }

    private static void LinkTwin(Dictionary<(int a, int b), BrepHalfEdge> edgeMap, BrepHalfEdge edge, int a, int b)
    {
        if (edgeMap.TryGetValue((b, a), out BrepHalfEdge? twin))
        {
            edge.Twin = twin;
            twin.Twin = edge;
        }
        else
        {
            edgeMap[(a, b)] = edge;
        }
    }
}
