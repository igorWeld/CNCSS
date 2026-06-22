using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Logic.Voxel.Adaptive;

/// <summary>
/// Менеджер октодерева чанков. Создаёт листья лениво при резании.
/// </summary>
public sealed class OctreeManager
{
    private readonly HashSet<VoxelChunk> _dirtyChunks = new();
    private int _structureVersion;

    public OctreeManager(
        StockVolumeConfig stockBounds,
        double coarseVoxelSizeMm = 1.0,
        double finestVoxelSizeMm = 0.1)
    {
        if (coarseVoxelSizeMm <= 0 || finestVoxelSizeMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(coarseVoxelSizeMm));
        }

        StockBounds = stockBounds;
        RootBounds = ExpandToCube(stockBounds);
        CoarseVoxelSizeMm = coarseVoxelSizeMm;
        FinestVoxelSizeMm = finestVoxelSizeMm;
        MaxDepth = ComputeDepth(RootBounds, finestVoxelSizeMm);
        Root = new OctreeNode(RootBounds, 0, MaxDepth);
    }

    public StockVolumeConfig StockBounds { get; }

    public Rect3D RootBounds { get; }

    public double CoarseVoxelSizeMm { get; }

    public double FinestVoxelSizeMm { get; }

    public int MaxDepth { get; }

    public OctreeNode Root { get; }

    public int StructureVersion => _structureVersion;

    public void InitializeBoundaryChunks()
    {
        double chunkSpan = VoxelChunk.Size * CoarseVoxelSizeMm;
        double minX = StockBounds.MinX;
        double minY = StockBounds.MinY;
        double minZ = StockBounds.MinZ;
        double maxX = StockBounds.MaxX;
        double maxY = StockBounds.MaxY;
        double maxZ = StockBounds.MaxZ;

        for (double z = minZ; z <= maxZ; z += chunkSpan)
        {
            for (double y = minY; y <= maxY; y += chunkSpan)
            {
                GetOrCreateChunk(minX + 0.5 * CoarseVoxelSizeMm, y, z, CoarseVoxelSizeMm);
                GetOrCreateChunk(maxX - 0.5 * CoarseVoxelSizeMm, y, z, CoarseVoxelSizeMm);
            }
        }

        for (double z = minZ; z <= maxZ; z += chunkSpan)
        {
            for (double x = minX; x <= maxX; x += chunkSpan)
            {
                GetOrCreateChunk(x, minY + 0.5 * CoarseVoxelSizeMm, z, CoarseVoxelSizeMm);
                GetOrCreateChunk(x, maxY - 0.5 * CoarseVoxelSizeMm, z, CoarseVoxelSizeMm);
            }
        }

        for (double y = minY; y <= maxY; y += chunkSpan)
        {
            for (double x = minX; x <= maxX; x += chunkSpan)
            {
                GetOrCreateChunk(x, y, minZ + 0.5 * CoarseVoxelSizeMm, CoarseVoxelSizeMm);
                GetOrCreateChunk(x, y, maxZ - 0.5 * CoarseVoxelSizeMm, CoarseVoxelSizeMm);
            }
        }
    }

    public VoxelChunk GetOrCreateChunk(double x, double y, double z, double targetVoxelSizeMm)
    {
        var point = new Point3D(x, y, z);
        if (!IsInStock(point))
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Point outside stock bounds.");
        }

        OctreeNode node = Root;
        while (node.Depth < MaxDepth)
        {
            double voxelSize = EstimateVoxelSize(node);
            if (voxelSize <= targetVoxelSizeMm + 1e-9)
            {
                break;
            }

            if (node.Chunk != null)
            {
                SplitChunkToChildren(node);
            }

            node.EnsureChildren();
            node = SelectChild(node, point) ?? node;
        }

        if (node.Chunk == null)
        {
            double voxelSize = EstimateVoxelSize(node);
            node.Chunk = new VoxelChunk(
                new Point3D(node.Bounds.X, node.Bounds.Y, node.Bounds.Z),
                voxelSize,
                node.Depth,
                initiallyOccupied: true);
            _structureVersion++;
        }

        return node.Chunk;
    }

    public IReadOnlyList<VoxelChunk> GetChunksInAabb(Rect3D aabb)
    {
        var result = new List<VoxelChunk>();
        CollectChunks(Root, aabb, result);
        return result;
    }

    public IReadOnlyList<VoxelChunk> GetOrCreateChunksInAabb(Rect3D aabb, double targetVoxelSizeMm)
    {
        var created = new List<VoxelChunk>();
        MaterializeByAabb(Root, aabb, targetVoxelSizeMm, created);
        return created;
    }

    public IReadOnlyCollection<VoxelChunk> ConsumeDirtyChunks()
    {
        VoxelChunk[] dirty = _dirtyChunks.ToArray();
        _dirtyChunks.Clear();
        return dirty;
    }

    public void MarkDirty(VoxelChunk chunk) => _dirtyChunks.Add(chunk);

    public void RequeueDirtyChunks(IEnumerable<VoxelChunk> chunks)
    {
        foreach (VoxelChunk chunk in chunks)
        {
            _dirtyChunks.Add(chunk);
        }
    }

    public IReadOnlyList<VoxelChunk> EnumerateAllChunks()
    {
        var chunks = new List<VoxelChunk>();
        CollectAll(Root, chunks);
        return chunks;
    }

    public bool TryGetChunkContaining(Point3D point, out VoxelChunk? chunk)
    {
        chunk = null;
        if (!IsInStock(point))
        {
            return false;
        }

        OctreeNode node = Root;
        while (true)
        {
            if (node.Chunk != null)
            {
                chunk = node.Chunk;
            }

            if (node.Children == null)
            {
                break;
            }

            OctreeNode? child = SelectChild(node, point);
            if (child == null)
            {
                break;
            }

            node = child;
        }

        return chunk != null;
    }

    private static Rect3D ExpandToCube(StockVolumeConfig bounds)
    {
        double sx = Math.Max(1e-6, bounds.MaxX - bounds.MinX);
        double sy = Math.Max(1e-6, bounds.MaxY - bounds.MinY);
        double sz = Math.Max(1e-6, bounds.MaxZ - bounds.MinZ);
        double side = Math.Max(sx, Math.Max(sy, sz));
        return new Rect3D(bounds.MinX, bounds.MinY, bounds.MinZ, side, side, side);
    }

    private static int ComputeDepth(Rect3D rootBounds, double finestVoxel)
    {
        int depth = 0;
        double current = Math.Max(1e-9, rootBounds.SizeX / VoxelChunk.Size);
        while (current > finestVoxel && depth < 12)
        {
            current *= 0.5;
            depth++;
        }

        return Math.Max(0, depth);
    }

    private bool IsInStock(Point3D p) =>
        p.X >= StockBounds.MinX && p.X <= StockBounds.MaxX &&
        p.Y >= StockBounds.MinY && p.Y <= StockBounds.MaxY &&
        p.Z >= StockBounds.MinZ && p.Z <= StockBounds.MaxZ;

    private static double EstimateVoxelSize(OctreeNode node) =>
        node.Bounds.SizeX / VoxelChunk.Size;

    private static OctreeNode? SelectChild(OctreeNode node, Point3D point)
    {
        if (node.Children == null)
        {
            return null;
        }

        foreach (OctreeNode child in node.Children)
        {
            if (child.Contains(point))
            {
                return child;
            }
        }

        return node.Children[0];
    }

    private void MaterializeByAabb(
        OctreeNode node,
        Rect3D aabb,
        double targetVoxelSizeMm,
        List<VoxelChunk> created)
    {
        if (!node.Intersects(aabb))
        {
            return;
        }

        double voxel = EstimateVoxelSize(node);
        if (node.Depth >= MaxDepth || voxel <= targetVoxelSizeMm + 1e-9)
        {
            Point3D center = new(
                node.Bounds.X + node.Bounds.SizeX * 0.5,
                node.Bounds.Y + node.Bounds.SizeY * 0.5,
                node.Bounds.Z + node.Bounds.SizeZ * 0.5);
            if (!IsInStock(center))
            {
                center = new Point3D(
                    Math.Clamp(center.X, StockBounds.MinX + 1e-6, StockBounds.MaxX - 1e-6),
                    Math.Clamp(center.Y, StockBounds.MinY + 1e-6, StockBounds.MaxY - 1e-6),
                    Math.Clamp(center.Z, StockBounds.MinZ + 1e-6, StockBounds.MaxZ - 1e-6));
            }

            created.Add(GetOrCreateChunk(center.X, center.Y, center.Z, targetVoxelSizeMm));
            return;
        }

        if (node.Chunk != null)
        {
            SplitChunkToChildren(node);
        }

        node.EnsureChildren();
        if (node.Children == null)
        {
            return;
        }

        foreach (OctreeNode child in node.Children)
        {
            MaterializeByAabb(child, aabb, targetVoxelSizeMm, created);
        }
    }

    private static void CollectChunks(OctreeNode node, Rect3D aabb, List<VoxelChunk> outChunks)
    {
        if (!node.Intersects(aabb))
        {
            return;
        }

        if (node.Chunk != null)
        {
            outChunks.Add(node.Chunk);
        }

        if (node.Children == null)
        {
            return;
        }

        foreach (OctreeNode child in node.Children)
        {
            CollectChunks(child, aabb, outChunks);
        }
    }

    private static void CollectAll(OctreeNode node, List<VoxelChunk> chunks)
    {
        if (node.Chunk != null)
        {
            chunks.Add(node.Chunk);
        }

        if (node.Children == null)
        {
            return;
        }

        foreach (OctreeNode child in node.Children)
        {
            CollectAll(child, chunks);
        }
    }

    private void SplitChunkToChildren(OctreeNode node)
    {
        VoxelChunk? parent = node.Chunk;
        if (parent == null)
        {
            return;
        }

        node.EnsureChildren();
        if (node.Children == null)
        {
            return;
        }

        foreach (OctreeNode child in node.Children)
        {
            VoxelChunk childChunk = CreateChildChunkFromParent(parent, child);
            child.Chunk = childChunk;
            MarkDirty(childChunk);
        }

        node.Chunk = null;
        _structureVersion++;
    }

    private static VoxelChunk CreateChildChunkFromParent(VoxelChunk parent, OctreeNode childNode)
    {
        double childVoxel = childNode.Bounds.SizeX / VoxelChunk.Size;
        bool initiallyOccupied = parent.CompressionKind == VoxelChunkCompressionKind.Full;
        var childChunk = new VoxelChunk(
            new Point3D(childNode.Bounds.X, childNode.Bounds.Y, childNode.Bounds.Z),
            childVoxel,
            childNode.Depth,
            initiallyOccupied: initiallyOccupied);

        if (parent.CompressionKind == VoxelChunkCompressionKind.Full)
        {
            return childChunk;
        }

        if (parent.CompressionKind == VoxelChunkCompressionKind.Empty)
        {
            return childChunk;
        }

        double pvs = parent.VoxelSizeMm;
        for (int z = 0; z < VoxelChunk.Size; z++)
        {
            for (int y = 0; y < VoxelChunk.Size; y++)
            {
                for (int x = 0; x < VoxelChunk.Size; x++)
                {
                    double wx = childChunk.Origin.X + (x + 0.5) * childVoxel;
                    double wy = childChunk.Origin.Y + (y + 0.5) * childVoxel;
                    double wz = childChunk.Origin.Z + (z + 0.5) * childVoxel;
                    int px = Math.Clamp((int)Math.Floor((wx - parent.Origin.X) / pvs), 0, VoxelChunk.Size - 1);
                    int py = Math.Clamp((int)Math.Floor((wy - parent.Origin.Y) / pvs), 0, VoxelChunk.Size - 1);
                    int pz = Math.Clamp((int)Math.Floor((wz - parent.Origin.Z) / pvs), 0, VoxelChunk.Size - 1);
                    bool occupied = parent.IsOccupied(px, py, pz);
                    childChunk.SetVoxel(x, y, z, occupied);
                }
            }
        }

        childChunk.TryCompress();
        childChunk.MarkClean();
        return childChunk;
    }
}
