using System.Collections.Concurrent;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Data.Config;
using HelixToolkit.Wpf;

namespace CNCSS.Logic.Voxel.Adaptive;

/// <summary>
/// Адаптивная воксельная заготовка: boundary-only init + lazy interior chunks + async mesh.
/// </summary>
public sealed class AdaptiveVoxelStock
{
    private readonly Dictionary<VoxelChunk, MeshGeometry3D> _chunkMeshCache = new();
    private readonly object _cacheGate = new();
    private readonly object _stateGate = new();
    private bool _fullMeshDirty = true;
    private int _knownStructureVersion;
    private bool _needsCachePrune;

    public AdaptiveVoxelStock(
        StockVolumeConfig bounds,
        double coarseVoxelMm = 1.0,
        double fineVoxelMm = 0.1)
    {
        Bounds = bounds;
        Octree = new OctreeManager(bounds, coarseVoxelMm, fineVoxelMm);
        CoarseVoxelMm = coarseVoxelMm;
        FineVoxelMm = fineVoxelMm;
        _knownStructureVersion = Octree.StructureVersion;
        _needsCachePrune = false;
    }

    public StockVolumeConfig Bounds { get; }

    public OctreeManager Octree { get; }

    public double CoarseVoxelMm { get; }

    public double FineVoxelMm { get; }

    public int LastRemovedVoxels { get; private set; }

    public void InitializeStock()
    {
        lock (_stateGate)
        {
            Octree.InitializeBoundaryChunks();
            foreach (VoxelChunk chunk in Octree.EnumerateAllChunks())
            {
                Octree.MarkDirty(chunk);
            }

            _fullMeshDirty = true;
            _knownStructureVersion = Octree.StructureVersion;
            _needsCachePrune = true;
        }
    }

    public int CutCylinder(Point3D start, Point3D end, double radius, double cutLength)
    {
        if (radius <= 0 || cutLength <= 0)
        {
            LastRemovedVoxels = 0;
            return 0;
        }

        lock (_stateGate)
        {
            int structureBefore = Octree.StructureVersion;
            Rect3D sweep = BuildSweepAabb(start, end, radius, cutLength);
            IReadOnlyList<VoxelChunk> chunks = Octree.GetOrCreateChunksInAabb(sweep, FineVoxelMm);
            int removed = 0;

            foreach (VoxelChunk chunk in chunks)
            {
                removed += CutChunk(chunk, start, end, radius, cutLength);
            }

            int structureAfter = Octree.StructureVersion;
            bool structureChanged = structureAfter != structureBefore;
            if (structureChanged)
            {
                _knownStructureVersion = structureAfter;
                _needsCachePrune = true;
            }

            LastRemovedVoxels = removed;
            return removed;
        }
    }

    public async Task<MeshGeometry3D> BuildMeshAsync(
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null,
        int displayStride = 1)
    {
        return await Task.Run(() =>
        {
            lock (_stateGate)
            {
                if (Octree.StructureVersion != _knownStructureVersion)
                {
                    _knownStructureVersion = Octree.StructureVersion;
                    _needsCachePrune = true;
                }

                if (_needsCachePrune)
                {
                    PruneStaleChunkCacheEntries();
                    _needsCachePrune = false;
                }

                IReadOnlyCollection<VoxelChunk> dirty = _fullMeshDirty
                    ? Octree.EnumerateAllChunks()
                    : Octree.ConsumeDirtyChunks();
                VoxelChunk[] dirtyBatch = dirty.ToArray();

                if (!_fullMeshDirty && dirtyBatch.Length > VoxelConstants.MaxDirtyChunksPerVisualTick)
                {
                    VoxelChunk[] tail = dirtyBatch.Skip(VoxelConstants.MaxDirtyChunksPerVisualTick).ToArray();
                    Octree.RequeueDirtyChunks(tail);
                    dirtyBatch = dirtyBatch.Take(VoxelConstants.MaxDirtyChunksPerVisualTick).ToArray();
                }

                if (dirtyBatch.Length > 0)
                {
                    if (_fullMeshDirty)
                    {
                        lock (_cacheGate)
                        {
                            _chunkMeshCache.Clear();
                        }
                    }

                    MeshGeometry3D[] built = BuildDirtyChunkMeshes(dirtyBatch, cancellationToken, progress, displayStride);
                    int i = 0;
                    lock (_cacheGate)
                    {
                        foreach (VoxelChunk chunk in dirtyBatch)
                        {
                            MeshGeometry3D mesh = built[i++];
                            if (mesh.Positions.Count == 0 || mesh.TriangleIndices.Count == 0)
                            {
                                _chunkMeshCache.Remove(chunk);
                            }
                            else
                            {
                                _chunkMeshCache[chunk] = mesh;
                            }

                            chunk.MarkClean();
                        }
                    }
                }

                _fullMeshDirty = false;

                return MergeChunkCache();
            }
        }, cancellationToken);
    }

    private void PruneStaleChunkCacheEntries()
    {
        HashSet<VoxelChunk> alive = Octree.EnumerateAllChunks().ToHashSet();
        lock (_cacheGate)
        {
            VoxelChunk[] cached = _chunkMeshCache.Keys.ToArray();
            foreach (VoxelChunk chunk in cached)
            {
                if (!alive.Contains(chunk))
                {
                    _chunkMeshCache.Remove(chunk);
                }
            }
        }
    }

    public AdaptiveVoxelStockState ExportState()
    {
        var chunks = new List<AdaptiveChunkState>();
        foreach (VoxelChunk chunk in Octree.EnumerateAllChunks())
        {
            chunks.Add(new AdaptiveChunkState(
                chunk.Origin.X,
                chunk.Origin.Y,
                chunk.Origin.Z,
                chunk.VoxelSizeMm,
                chunk.LodLevel,
                chunk.ExportBitArray().Cast<bool>().ToArray()));
        }

        return new AdaptiveVoxelStockState(Bounds, CoarseVoxelMm, FineVoxelMm, chunks);
    }

    public static AdaptiveVoxelStock ImportState(AdaptiveVoxelStockState state)
    {
        var stock = new AdaptiveVoxelStock(state.Bounds, state.CoarseVoxelMm, state.FineVoxelMm);
        foreach (AdaptiveChunkState chunkState in state.Chunks)
        {
            Point3D center = new(
                chunkState.OriginX + chunkState.VoxelSizeMm * VoxelChunk.Size * 0.5,
                chunkState.OriginY + chunkState.VoxelSizeMm * VoxelChunk.Size * 0.5,
                chunkState.OriginZ + chunkState.VoxelSizeMm * VoxelChunk.Size * 0.5);
            VoxelChunk chunk = stock.Octree.GetOrCreateChunk(center.X, center.Y, center.Z, chunkState.VoxelSizeMm);
            chunk.ImportBitArray(new System.Collections.BitArray(chunkState.Occupancy));
            stock.Octree.MarkDirty(chunk);
        }

        stock._fullMeshDirty = true;
        stock._knownStructureVersion = stock.Octree.StructureVersion;
        return stock;
    }

    private MeshGeometry3D[] BuildDirtyChunkMeshes(
        IReadOnlyCollection<VoxelChunk> dirtyChunks,
        CancellationToken ct,
        IProgress<double>? progress,
        int displayStride)
    {
        VoxelChunk[] chunks = dirtyChunks.ToArray();
        var result = new MeshGeometry3D[chunks.Length];
        for (int i = 0; i < chunks.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            result[i] = BuildChunkMeshGlobal(chunks[i], displayStride);
            progress?.Report((double)(i + 1) / Math.Max(1, chunks.Length));
        }

        return result;
    }

    private MeshGeometry3D BuildChunkMeshGlobal(VoxelChunk chunk, int displayStride)
    {
        var builder = new MeshBuilder(false, false);
        double vs = chunk.VoxelSizeMm;
        int stride = Math.Max(1, displayStride);
        if (stride <= 1)
        {
            chunk.ForEachOccupied((lx, ly, lz) =>
            {
                double x0 = chunk.Origin.X + lx * vs;
                double y0 = chunk.Origin.Y + ly * vs;
                double z0 = chunk.Origin.Z + lz * vs;
                double x1 = x0 + vs;
                double y1 = y0 + vs;
                double z1 = z0 + vs;

                if (!IsOccupiedGlobal(new Point3D(x1 + vs * 0.5, y0 + vs * 0.5, z0 + vs * 0.5)))
                {
                    AddQuad(builder, x1, y0, z0, x1, y1, z0, x1, y1, z1, x1, y0, z1);
                }

                if (!IsOccupiedGlobal(new Point3D(x0 - vs * 0.5, y0 + vs * 0.5, z0 + vs * 0.5)))
                {
                    AddQuad(builder, x0, y0, z1, x0, y1, z1, x0, y1, z0, x0, y0, z0);
                }

                if (!IsOccupiedGlobal(new Point3D(x0 + vs * 0.5, y1 + vs * 0.5, z0 + vs * 0.5)))
                {
                    AddQuad(builder, x0, y1, z0, x1, y1, z0, x1, y1, z1, x0, y1, z1);
                }

                if (!IsOccupiedGlobal(new Point3D(x0 + vs * 0.5, y0 - vs * 0.5, z0 + vs * 0.5)))
                {
                    AddQuad(builder, x0, y0, z1, x1, y0, z1, x1, y0, z0, x0, y0, z0);
                }

                if (!IsOccupiedGlobal(new Point3D(x0 + vs * 0.5, y0 + vs * 0.5, z1 + vs * 0.5)))
                {
                    AddQuad(builder, x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1);
                }

                if (!IsOccupiedGlobal(new Point3D(x0 + vs * 0.5, y0 + vs * 0.5, z0 - vs * 0.5)))
                {
                    AddQuad(builder, x0, y1, z0, x1, y1, z0, x1, y0, z0, x0, y0, z0);
                }
            });
        }
        else
        {
            int bxCount = (VoxelChunk.Size + stride - 1) / stride;
            int byCount = (VoxelChunk.Size + stride - 1) / stride;
            int bzCount = (VoxelChunk.Size + stride - 1) / stride;
            var coarse = new bool[bxCount, byCount, bzCount];

            for (int bz = 0; bz < bzCount; bz++)
            {
                int zStart = bz * stride;
                int zLen = Math.Min(stride, VoxelChunk.Size - zStart);
                for (int by = 0; by < byCount; by++)
                {
                    int yStart = by * stride;
                    int yLen = Math.Min(stride, VoxelChunk.Size - yStart);
                    for (int bx = 0; bx < bxCount; bx++)
                    {
                        int xStart = bx * stride;
                        int xLen = Math.Min(stride, VoxelChunk.Size - xStart);
                        coarse[bx, by, bz] = IsAnyOccupiedInBlock(chunk, xStart, xLen, yStart, yLen, zStart, zLen);
                    }
                }
            }

            for (int bz = 0; bz < bzCount; bz++)
            {
                int zStart = bz * stride;
                int zLen = Math.Min(stride, VoxelChunk.Size - zStart);
                double z0 = chunk.Origin.Z + zStart * vs;
                double z1 = z0 + zLen * vs;

                for (int by = 0; by < byCount; by++)
                {
                    int yStart = by * stride;
                    int yLen = Math.Min(stride, VoxelChunk.Size - yStart);
                    double y0 = chunk.Origin.Y + yStart * vs;
                    double y1 = y0 + yLen * vs;

                    for (int bx = 0; bx < bxCount; bx++)
                    {
                        if (!coarse[bx, by, bz])
                        {
                            continue;
                        }

                        int xStart = bx * stride;
                        int xLen = Math.Min(stride, VoxelChunk.Size - xStart);
                        double x0 = chunk.Origin.X + xStart * vs;
                        double x1 = x0 + xLen * vs;

                        if (!HasCoarseNeighborOccupied(coarse, bx, by, bz, 1, 0, 0, x1, (y0 + y1) * 0.5, (z0 + z1) * 0.5, vs))
                        {
                            AddQuad(builder, x1, y0, z0, x1, y1, z0, x1, y1, z1, x1, y0, z1);
                        }

                        if (!HasCoarseNeighborOccupied(coarse, bx, by, bz, -1, 0, 0, x0, (y0 + y1) * 0.5, (z0 + z1) * 0.5, vs))
                        {
                            AddQuad(builder, x0, y0, z1, x0, y1, z1, x0, y1, z0, x0, y0, z0);
                        }

                        if (!HasCoarseNeighborOccupied(coarse, bx, by, bz, 0, 1, 0, (x0 + x1) * 0.5, y1, (z0 + z1) * 0.5, vs))
                        {
                            AddQuad(builder, x0, y1, z0, x1, y1, z0, x1, y1, z1, x0, y1, z1);
                        }

                        if (!HasCoarseNeighborOccupied(coarse, bx, by, bz, 0, -1, 0, (x0 + x1) * 0.5, y0, (z0 + z1) * 0.5, vs))
                        {
                            AddQuad(builder, x0, y0, z1, x1, y0, z1, x1, y0, z0, x0, y0, z0);
                        }

                        if (!HasCoarseNeighborOccupied(coarse, bx, by, bz, 0, 0, 1, (x0 + x1) * 0.5, (y0 + y1) * 0.5, z1, vs))
                        {
                            AddQuad(builder, x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1);
                        }

                        if (!HasCoarseNeighborOccupied(coarse, bx, by, bz, 0, 0, -1, (x0 + x1) * 0.5, (y0 + y1) * 0.5, z0, vs))
                        {
                            AddQuad(builder, x0, y1, z0, x1, y1, z0, x1, y0, z0, x0, y0, z0);
                        }
                    }
                }
            }
        }

        MeshGeometry3D mesh = builder.ToMesh();
        mesh.Freeze();
        return mesh;
    }

    private static bool IsAnyOccupiedInBlock(
        VoxelChunk chunk,
        int xStart,
        int xLen,
        int yStart,
        int yLen,
        int zStart,
        int zLen)
    {
        for (int z = zStart; z < zStart + zLen; z++)
        {
            for (int y = yStart; y < yStart + yLen; y++)
            {
                for (int x = xStart; x < xStart + xLen; x++)
                {
                    if (chunk.IsOccupied(x, y, z))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool HasCoarseNeighborOccupied(
        bool[,,] coarse,
        int bx,
        int by,
        int bz,
        int dx,
        int dy,
        int dz,
        double faceX,
        double faceY,
        double faceZ,
        double voxelSizeMm)
    {
        int nx = bx + dx;
        int ny = by + dy;
        int nz = bz + dz;
        if (nx >= 0 && nx < coarse.GetLength(0) &&
            ny >= 0 && ny < coarse.GetLength(1) &&
            nz >= 0 && nz < coarse.GetLength(2))
        {
            return coarse[nx, ny, nz];
        }

        // Запрос к соседнему чанку: семпл "снаружи" текущей грани.
        Point3D probe = new(
            faceX + dx * voxelSizeMm * 0.5,
            faceY + dy * voxelSizeMm * 0.5,
            faceZ + dz * voxelSizeMm * 0.5);
        return IsOccupiedGlobal(probe);
    }

    private bool IsOccupiedGlobal(Point3D point)
    {
        if (point.X < Bounds.MinX || point.X > Bounds.MaxX ||
            point.Y < Bounds.MinY || point.Y > Bounds.MaxY ||
            point.Z < Bounds.MinZ || point.Z > Bounds.MaxZ)
        {
            return false;
        }

        if (!Octree.TryGetChunkContaining(point, out VoxelChunk? chunk) || chunk == null)
        {
            // Не материализованный объём внутри заготовки считаем сплошным.
            return true;
        }

        double vs = chunk.VoxelSizeMm;
        int lx = Math.Clamp((int)Math.Floor((point.X - chunk.Origin.X) / vs), 0, VoxelChunk.Size - 1);
        int ly = Math.Clamp((int)Math.Floor((point.Y - chunk.Origin.Y) / vs), 0, VoxelChunk.Size - 1);
        int lz = Math.Clamp((int)Math.Floor((point.Z - chunk.Origin.Z) / vs), 0, VoxelChunk.Size - 1);
        return chunk.IsOccupied(lx, ly, lz);
    }

    private static void AddQuad(
        MeshBuilder builder,
        double x0, double y0, double z0,
        double x1, double y1, double z1,
        double x2, double y2, double z2,
        double x3, double y3, double z3)
    {
        builder.AddTriangle(new Point3D(x0, y0, z0), new Point3D(x1, y1, z1), new Point3D(x2, y2, z2));
        builder.AddTriangle(new Point3D(x0, y0, z0), new Point3D(x2, y2, z2), new Point3D(x3, y3, z3));
    }

    private MeshGeometry3D MergeChunkCache()
    {
        int totalPositions = 0;
        int totalIndices = 0;
        lock (_cacheGate)
        {
            foreach ((_, MeshGeometry3D mesh) in _chunkMeshCache)
            {
                totalPositions += mesh.Positions.Count;
                totalIndices += mesh.TriangleIndices.Count;
            }
        }

        var positions = new Point3DCollection(totalPositions);
        var indices = new Int32Collection(totalIndices);

        lock (_cacheGate)
        {
            foreach ((_, MeshGeometry3D mesh) in _chunkMeshCache)
            {
                int baseVertex = positions.Count;
                foreach (Point3D p in mesh.Positions)
                {
                    positions.Add(p);
                }

                foreach (int idx in mesh.TriangleIndices)
                {
                    indices.Add(baseVertex + idx);
                }
            }
        }

        var merged = new MeshGeometry3D
        {
            Positions = positions,
            TriangleIndices = indices
        };
        merged.Freeze();
        return merged;
    }

    private int CutChunk(VoxelChunk chunk, Point3D start, Point3D end, double radius, double cutLength)
    {
        int removed = 0;
        chunk.ForEachOccupied((lx, ly, lz) =>
        {
            double x = chunk.Origin.X + (lx + 0.5) * chunk.VoxelSizeMm;
            double y = chunk.Origin.Y + (ly + 0.5) * chunk.VoxelSizeMm;
            double z = chunk.Origin.Z + (lz + 0.5) * chunk.VoxelSizeMm;
            if (!IsInsideSweptCylinder(new Point3D(x, y, z), start, end, radius, cutLength))
            {
                return;
            }

            chunk.SetVoxel(lx, ly, lz, false);
            removed++;
        });

        if (removed > 0)
        {
            chunk.TryCompress();
            Octree.MarkDirty(chunk);
        }

        return removed;
    }

    private static bool IsInsideSweptCylinder(
        Point3D point,
        Point3D start,
        Point3D end,
        double radius,
        double cutLength)
    {
        Vector3D seg = end - start;
        double len2 = seg.LengthSquared;
        double t = 0.0;
        if (len2 > 1e-12)
        {
            t = ((point.X - start.X) * seg.X + (point.Y - start.Y) * seg.Y + (point.Z - start.Z) * seg.Z) / len2;
            t = Math.Clamp(t, 0.0, 1.0);
        }

        Point3D tip = start + seg * t;
        // Вертикальный шпиндель: торец инструмента = нижняя точка (tip.Z),
        // режущая часть располагается ВЫШЕ торца на длину cutLength.
        if (point.Z < tip.Z || point.Z > tip.Z + cutLength)
        {
            return false;
        }

        double dx = point.X - tip.X;
        double dy = point.Y - tip.Y;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static Rect3D BuildSweepAabb(Point3D start, Point3D end, double radius, double cutLength)
    {
        double minX = Math.Min(start.X, end.X) - radius;
        double maxX = Math.Max(start.X, end.X) + radius;
        double minY = Math.Min(start.Y, end.Y) - radius;
        double maxY = Math.Max(start.Y, end.Y) + radius;
        // Режущая зона: [tip.Z .. tip.Z + cutLength], добавляем небольшой запас по радиусу.
        double minZ = Math.Min(start.Z, end.Z) - radius;
        double maxZ = Math.Max(start.Z, end.Z) + cutLength;
        return new Rect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
    }
}

public sealed record AdaptiveChunkState(
    double OriginX,
    double OriginY,
    double OriginZ,
    double VoxelSizeMm,
    int LodLevel,
    bool[] Occupancy);

public sealed record AdaptiveVoxelStockState(
    StockVolumeConfig Bounds,
    double CoarseVoxelMm,
    double FineVoxelMm,
    IReadOnlyList<AdaptiveChunkState> Chunks);
