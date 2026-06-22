using System.Windows.Media.Media3D;

namespace CNCSS.Logic.Voxel.Adaptive;

/// <summary>Узел октодерева, покрывающий AABB подмножества заготовки.</summary>
public sealed class OctreeNode
{
    public OctreeNode(Rect3D bounds, int depth, int maxDepth)
    {
        Bounds = bounds;
        Depth = depth;
        MaxDepth = maxDepth;
    }

    public Rect3D Bounds { get; }

    public int Depth { get; }

    public int MaxDepth { get; }

    public VoxelChunk? Chunk { get; set; }

    public OctreeNode[]? Children { get; private set; }

    public bool IsLeaf => Children == null;

    public bool Intersects(Rect3D other) => Bounds.IntersectsWith(other);

    public bool Contains(Point3D point) =>
        point.X >= Bounds.X && point.X <= Bounds.X + Bounds.SizeX &&
        point.Y >= Bounds.Y && point.Y <= Bounds.Y + Bounds.SizeY &&
        point.Z >= Bounds.Z && point.Z <= Bounds.Z + Bounds.SizeZ;

    public void EnsureChildren()
    {
        if (Children != null || Depth >= MaxDepth)
        {
            return;
        }

        Children = new OctreeNode[8];
        double hx = Bounds.SizeX * 0.5;
        double hy = Bounds.SizeY * 0.5;
        double hz = Bounds.SizeZ * 0.5;

        int i = 0;
        for (int z = 0; z < 2; z++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    var child = new Rect3D(
                        Bounds.X + x * hx,
                        Bounds.Y + y * hy,
                        Bounds.Z + z * hz,
                        hx,
                        hy,
                        hz);
                    Children[i++] = new OctreeNode(child, Depth + 1, MaxDepth);
                }
            }
        }
    }
}
