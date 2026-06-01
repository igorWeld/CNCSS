using System.Windows.Media.Media3D;

namespace CNCSS.Geometry.BRep;

public sealed class BrepVertex
{
    public int Id { get; init; }
    public Point3D Position { get; init; }
}

public sealed class BrepHalfEdge
{
    public int Id { get; init; }
    public BrepVertex? Start { get; set; }
    public BrepHalfEdge? Next { get; set; }
    public BrepHalfEdge? Twin { get; set; }
    public BrepFace? Face { get; set; }
}

public sealed class BrepLoop
{
    public BrepHalfEdge? First { get; set; }
}

public sealed class BrepFace
{
    public int Id { get; init; }
    public BrepLoop Outer { get; } = new();
    public Vector3D Normal { get; set; }
}

public sealed class BrepSolid
{
    public IReadOnlyList<BrepVertex> Vertices { get; init; } = Array.Empty<BrepVertex>();
    public IReadOnlyList<BrepHalfEdge> HalfEdges { get; init; } = Array.Empty<BrepHalfEdge>();
    public IReadOnlyList<BrepFace> Faces { get; init; } = Array.Empty<BrepFace>();
    public ISolidField Field { get; init; } = new EmptyField();
    public MeshGeometry3D Mesh { get; set; } = new();
}

public interface ISolidField
{
    Rect3D Bounds { get; }
    bool Contains(Point3D p);
}

public sealed class EmptyField : ISolidField
{
    public Rect3D Bounds => Rect3D.Empty;
    public bool Contains(Point3D p) => false;
}
