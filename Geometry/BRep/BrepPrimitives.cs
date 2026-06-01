using System.Windows.Media.Media3D;
using System.Windows.Media;

namespace CNCSS.Geometry.BRep;

public static class BrepPrimitives
{
    public static BrepSolid CreateBox(Rect3D box)
    {
        var field = new BoxField(box);
        MeshGeometry3D mesh = BuildBoxMesh(box);
        return BrepFromMesh.Build(mesh, field);
    }

    private static MeshGeometry3D BuildBoxMesh(Rect3D box)
    {
        double x0 = box.X;
        double y0 = box.Y;
        double z0 = box.Z;
        double x1 = box.X + box.SizeX;
        double y1 = box.Y + box.SizeY;
        double z1 = box.Z + box.SizeZ;

        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(x0, y0, z0), // 0
                new(x1, y0, z0), // 1
                new(x1, y1, z0), // 2
                new(x0, y1, z0), // 3
                new(x0, y0, z1), // 4
                new(x1, y0, z1), // 5
                new(x1, y1, z1), // 6
                new(x0, y1, z1), // 7
            },
            TriangleIndices = new Int32Collection
            {
                0,2,1, 0,3,2, // bottom
                4,5,6, 4,6,7, // top
                0,1,5, 0,5,4, // front
                1,2,6, 1,6,5, // right
                2,3,7, 2,7,6, // back
                3,0,4, 3,4,7  // left
            }
        };
        return mesh;
    }
}

public sealed class BoxField : ISolidField
{
    public BoxField(Rect3D bounds) => Bounds = bounds;
    public Rect3D Bounds { get; }

    public bool Contains(Point3D p) =>
        p.X >= Bounds.X && p.X <= Bounds.X + Bounds.SizeX &&
        p.Y >= Bounds.Y && p.Y <= Bounds.Y + Bounds.SizeY &&
        p.Z >= Bounds.Z && p.Z <= Bounds.Z + Bounds.SizeZ;
}
