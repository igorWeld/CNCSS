using System;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using CNCSS.Data.Tools;
using CNCSS.Data;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CNCSS.Vis
{
    /// <summary>
    /// Представляет заготовку в виде воксельной сетки, оптимизированной чанками и Greedy Meshing.
    /// </summary>
    public class VoxelStock
    {
        private sealed class ColorMeshAccumulator
        {
            public Point3DCollection Positions { get; } = new Point3DCollection();
            public Int32Collection Indices { get; } = new Int32Collection();
        }

        private readonly VoxelChunk[,,] _chunks;
        private readonly double _resolution;
        private readonly Point3D _min;
        private readonly int _sizeX, _sizeY, _sizeZ;
        private readonly int _chunkCountX, _chunkCountY, _chunkCountZ;
        private readonly ReaderWriterLockSlim _voxelsLock = new ReaderWriterLockSlim();

        private readonly Dictionary<(int, int, int), Model3D> _chunkModels = new Dictionary<(int, int, int), Model3D>();
        private readonly Model3DGroup _modelGroup = new Model3DGroup();
        private const int ChunkMaskLength = VoxelChunk.Size * VoxelChunk.Size;
        private const uint DefaultSurfaceColor = 0xFF808080;
        [ThreadStatic] private static bool[]? s_chunkMask;
        [ThreadStatic] private static uint[]? s_chunkColorMask;

        public Model3D MainModel => _modelGroup;
        public bool IsDirty { get; set; } = true;

        public VoxelStock(double width, double depth, double height, double resolution, Point3D center, double maxZ)
        {
            _resolution = resolution;
            _sizeX = Math.Max(1, (int)(width / resolution));
            _sizeY = Math.Max(1, (int)(depth / resolution));
            _sizeZ = Math.Max(1, (int)(height / resolution));

            _chunkCountX = (_sizeX + VoxelChunk.Size - 1) / VoxelChunk.Size;
            _chunkCountY = (_sizeY + VoxelChunk.Size - 1) / VoxelChunk.Size;
            _chunkCountZ = (_sizeZ + VoxelChunk.Size - 1) / VoxelChunk.Size;

            _chunks = new VoxelChunk[_chunkCountX, _chunkCountY, _chunkCountZ];
            _min = new Point3D(center.X - width / 2, center.Y - depth / 2, maxZ - height);

            for (int z = 0; z < _chunkCountZ; z++)
                for (int y = 0; y < _chunkCountY; y++)
                    for (int x = 0; x < _chunkCountX; x++)
                        _chunks[x, y, z] = new VoxelChunk(true);

            IsDirty = true;
        }

        public void CutCylinder(Point3D start, Point3D end, double radius, double fluteLength, Color toolColor)
        {
            uint toolColorPacked = PackColor(toolColor);
            _voxelsLock.EnterWriteLock();
            try
            {
            double r2 = radius * radius;
            Vector3D dir = end - start;
            double len2_xy = dir.X * dir.X + dir.Y * dir.Y;

            int minX = Math.Max(0, (int)((Math.Min(start.X, end.X) - radius - _min.X) / _resolution));
            int maxX = Math.Min(_sizeX - 1, (int)((Math.Max(start.X, end.X) + radius - _min.X) / _resolution));
            int minY = Math.Max(0, (int)((Math.Min(start.Y, end.Y) - radius - _min.Y) / _resolution));
            int maxY = Math.Min(_sizeY - 1, (int)((Math.Max(start.Y, end.Y) + radius - _min.Y) / _resolution));
            int minZ = Math.Max(0, (int)((Math.Min(start.Z, end.Z) - _min.Z) / _resolution));
            int maxZ = Math.Min(_sizeZ - 1, (int)((Math.Max(start.Z, end.Z) + fluteLength - _min.Z) / _resolution));

            int minCX = Math.Max(0, minX / VoxelChunk.Size);
            int maxCX = Math.Min(_chunkCountX - 1, maxX / VoxelChunk.Size);
            int minCY = Math.Max(0, minY / VoxelChunk.Size);
            int maxCY = Math.Min(_chunkCountY - 1, maxY / VoxelChunk.Size);
            int minCZ = Math.Max(0, minZ / VoxelChunk.Size);
            int maxCZ = Math.Min(_chunkCountZ - 1, maxZ / VoxelChunk.Size);

            if (minCX > maxCX || minCY > maxCY || minCZ > maxCZ) return;

            Parallel.For(minCX, maxCX + 1, cx =>
            {
                for (int cy = minCY; cy <= maxCY; cy++)
                {
                    for (int cz = minCZ; cz <= maxCZ; cz++)
                    {
                        var chunk = _chunks[cx, cy, cz];
                        if (chunk == null || chunk.IsEmpty) continue;

                        bool chunkModified = false;
                        int xStart = cx * VoxelChunk.Size;
                        int yStart = cy * VoxelChunk.Size;
                        int zStartChunk = cz * VoxelChunk.Size;

                        for (int lx = 0; lx < VoxelChunk.Size; lx++)
                        {
                            int gx = xStart + lx;
                            if (gx > maxX || gx < minX) continue;
                            double px = _min.X + gx * _resolution;

                            for (int ly = 0; ly < VoxelChunk.Size; ly++)
                            {
                                int gy = yStart + ly;
                                if (gy > maxY || gy < minY) continue;
                                double py = _min.Y + gy * _resolution;

                                double t = len2_xy < ProjectConstants.EPSILON ? 0 : Math.Max(0, Math.Min(1, ((px - start.X) * dir.X + (py - start.Y) * dir.Y) / (len2_xy + ProjectConstants.SMALL_EPSILON)));
                                double dx = px - (start.X + t * dir.X);
                                double dy = py - (start.Y + t * dir.Y);

                                if (dx * dx + dy * dy <= r2)
                                {
                                    double currentTipZ = start.Z + t * dir.Z;
                                    double currentTopZ = currentTipZ + fluteLength;

                                    int gzStart = Math.Max(zStartChunk, (int)((currentTipZ - _min.Z) / _resolution));
                                    int gzEnd = Math.Min(zStartChunk + VoxelChunk.Size - 1, (int)((currentTopZ - _min.Z) / _resolution));

                                    for (int gz = gzStart; gz <= gzEnd; gz++)
                                    {
                                        int lz = gz - zStartChunk;
                                        // Оптимизация: проверяем наличие вокселя перед записью
                                        if (chunk.GetVoxel(lx, ly, lz))
                                        {
                                            chunk.SetVoxel(lx, ly, lz, false, toolColorPacked);
                                            chunkModified = true;
                                        }
                                    }
                                }
                            }
                        }
                        if (chunkModified) IsDirty = true;
                    }
                }
            });
            }
            finally
            {
                _voxelsLock.ExitWriteLock();
            }
        }

        public async Task UpdateVisualsAsync()
        {
            if (_isUpdating) return;
            _isUpdating = true;

            try
            {
                var dirty = new List<(int cx, int cy, int cz, bool isEmpty)>();

                for (int cz = 0; cz < _chunkCountZ; cz++)
                {
                    for (int cy = 0; cy < _chunkCountY; cy++)
                    {
                        for (int cx = 0; cx < _chunkCountX; cx++)
                        {
                            var chunk = _chunks[cx, cy, cz];
                            if (chunk.IsDirty)
                            {
                                chunk.IsDirty = false;
                                dirty.Add((cx, cy, cz, chunk.IsEmpty));
                            }
                        }
                    }
                }

                if (dirty.Count > 0)
                {
                    var results = new (int cx, int cy, int cz, List<(uint color, MeshGeometry3D mesh)>? meshes)[dirty.Count];

                    await Task.Run(() =>
                    {
                        Parallel.For(
                            0,
                            dirty.Count,
                            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                            i =>
                            {
                                var item = dirty[i];
                                if (item.isEmpty)
                                {
                                    results[i] = (item.cx, item.cy, item.cz, null);
                                    return;
                                }

                                var meshes = CreateChunkMeshes(item.cx, item.cy, item.cz);
                                results[i] = (item.cx, item.cy, item.cz, meshes);
                            });
                    });

                    // Оптимизация: Накапливаем изменения для пакетного обновления UI
                    var toRemove = new List<Model3D>();
                    var toAdd = new List<Model3D>();

                    foreach (var (cx, cy, cz, meshes) in results)
                    {
                        var key = (cx, cy, cz);
                        if (meshes == null || meshes.Count == 0)
                        {
                            if (_chunkModels.TryGetValue(key, out var model))
                            {
                                toRemove.Add(model);
                                _chunkModels.Remove(key);
                            }
                        }
                        else
                        {
                            var modelGroup = new Model3DGroup();
                            foreach (var (color, mesh) in meshes)
                            {
                                var material = BuildMaterial(color);
                                var part = new GeometryModel3D(mesh, material)
                                {
                                    BackMaterial = material
                                };
                                modelGroup.Children.Add(part);
                            }

                            if (_chunkModels.TryGetValue(key, out var existingModel))
                            {
                                toRemove.Add(existingModel);
                            }

                            _chunkModels[key] = modelGroup;
                            toAdd.Add(modelGroup);
                        }
                    }

                    // Пакетное применение изменений к Model3DGroup (минимизирует фризы UI)
                    if (toRemove.Count > 0 || toAdd.Count > 0)
                    {
                        foreach (var model in toRemove) _modelGroup.Children.Remove(model);
                        foreach (var model in toAdd) _modelGroup.Children.Add(model);
                    }
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private bool _isUpdating = false;

        private List<(uint color, MeshGeometry3D mesh)> CreateChunkMeshes(int cx, int cy, int cz)
        {
            _voxelsLock.EnterReadLock();
            try
            {
            var accumulators = new Dictionary<uint, ColorMeshAccumulator>();
            double res = _resolution;

            int xBase = cx * VoxelChunk.Size;
            int yBase = cy * VoxelChunk.Size;
            int zBase = cz * VoxelChunk.Size;

            for (int d = 0; d < 6; d++)
            {
                int axis = d / 2;
                bool positive = d % 2 == 1;

                for (int i = 0; i < VoxelChunk.Size; i++)
                {
                    int gi = (axis == 0 ? xBase : axis == 1 ? yBase : zBase) + i;
                    if (gi < 0 || gi >= (axis == 0 ? _sizeX : axis == 1 ? _sizeY : _sizeZ)) continue;

                    bool[] mask = s_chunkMask ??= new bool[ChunkMaskLength];
                    uint[] colorMask = s_chunkColorMask ??= new uint[ChunkMaskLength];
                    Array.Clear(mask, 0, mask.Length);
                    Array.Clear(colorMask, 0, colorMask.Length);
                    bool hasAny = false;

                    for (int v = 0; v < VoxelChunk.Size; v++)
                    {
                        for (int u = 0; u < VoxelChunk.Size; u++)
                        {
                            int lx = axis == 0 ? i : (axis == 1 ? u : u);
                            int ly = axis == 0 ? u : (axis == 1 ? i : v);
                            int lz = axis == 0 ? v : (axis == 1 ? v : i);

                            int gx = xBase + lx;
                            int gy = yBase + ly;
                            int gz = zBase + lz;

                            if (IsVoxelPresent(gx, gy, gz))
                            {
                                int nx = gx + (axis == 0 ? (positive ? 1 : -1) : 0);
                                int ny = gy + (axis == 1 ? (positive ? 1 : -1) : 0);
                                int nz = gz + (axis == 2 ? (positive ? 1 : -1) : 0);

                                if (!IsVoxelPresent(nx, ny, nz))
                                {
                                    uint faceColor = GetFaceColor(gx, gy, gz, nx, ny, nz);
                                    mask[u + v * VoxelChunk.Size] = true;
                                    colorMask[u + v * VoxelChunk.Size] = faceColor;
                                    hasAny = true;
                                }
                            }
                        }
                    }

                    if (!hasAny) continue;

                    for (int v = 0; v < VoxelChunk.Size; v++)
                    {
                        for (int u = 0; u < VoxelChunk.Size; u++)
                        {
                            if (mask[u + v * VoxelChunk.Size])
                            {
                                uint currentColor = colorMask[u + v * VoxelChunk.Size];
                                int w, h;
                                for (w = 1; u + w < VoxelChunk.Size; w++)
                                {
                                    int idx = u + w + v * VoxelChunk.Size;
                                    if (!mask[idx] || colorMask[idx] != currentColor)
                                    {
                                        break;
                                    }
                                }
                                bool done = false;
                                for (h = 1; v + h < VoxelChunk.Size; h++)
                                {
                                    for (int k = 0; k < w; k++)
                                    {
                                        int idx = u + k + (v + h) * VoxelChunk.Size;
                                        if (!mask[idx] || colorMask[idx] != currentColor) { done = true; break; }
                                    }
                                    if (done) break;
                                }

                                int faceU = (axis == 0 ? yBase : xBase) + u;
                                int faceV = (axis == 2 ? yBase : zBase) + v;
                                if (!accumulators.TryGetValue(currentColor, out var acc))
                                {
                                    acc = new ColorMeshAccumulator();
                                    accumulators[currentColor] = acc;
                                }

                                AddGreedyFace(acc.Positions, acc.Indices, axis, positive, gi, faceU, faceV, w, h);

                                for (int l = 0; l < h; l++)
                                    for (int k = 0; k < w; k++)
                                    {
                                        int idx = u + k + (v + l) * VoxelChunk.Size;
                                        mask[idx] = false;
                                        colorMask[idx] = 0;
                                    }
                            }
                        }
                    }
                }
            }

            var result = new List<(uint color, MeshGeometry3D mesh)>(accumulators.Count);
            foreach (var pair in accumulators)
            {
                var mesh = new MeshGeometry3D
                {
                    Positions = pair.Value.Positions,
                    TriangleIndices = pair.Value.Indices
                };
                mesh.Freeze();
                result.Add((pair.Key, mesh));
            }

            return result;
            }
            finally
            {
                _voxelsLock.ExitReadLock();
            }
        }

        private void AddGreedyFace(Point3DCollection pos, Int32Collection idx, int axis, bool positive, int i, int u, int v, int w, int h)
        {
            int baseIdx = pos.Count;
            double res = _resolution;
            double offset = positive ? res : 0;

            Point3D p1, p2, p3, p4;
            if (axis == 0) // X
            {
                p1 = new Point3D(_min.X + i * res + offset, _min.Y + u * res, _min.Z + v * res);
                p2 = new Point3D(_min.X + i * res + offset, _min.Y + (u + w) * res, _min.Z + v * res);
                p3 = new Point3D(_min.X + i * res + offset, _min.Y + (u + w) * res, _min.Z + (v + h) * res);
                p4 = new Point3D(_min.X + i * res + offset, _min.Y + u * res, _min.Z + (v + h) * res);
            }
            else if (axis == 1) // Y
            {
                p1 = new Point3D(_min.X + u * res, _min.Y + i * res + offset, _min.Z + v * res);
                p2 = new Point3D(_min.X + u * res, _min.Y + i * res + offset, _min.Z + (v + h) * res);
                p3 = new Point3D(_min.X + (u + w) * res, _min.Y + i * res + offset, _min.Z + (v + h) * res);
                p4 = new Point3D(_min.X + (u + w) * res, _min.Y + i * res + offset, _min.Z + v * res);
            }
            else // Z
            {
                p1 = new Point3D(_min.X + u * res, _min.Y + v * res, _min.Z + i * res + offset);
                p2 = new Point3D(_min.X + (u + w) * res, _min.Y + v * res, _min.Z + i * res + offset);
                p3 = new Point3D(_min.X + (u + w) * res, _min.Y + (v + h) * res, _min.Z + i * res + offset);
                p4 = new Point3D(_min.X + u * res, _min.Y + (v + h) * res, _min.Z + i * res + offset);
            }

            pos.Add(p1); pos.Add(p2); pos.Add(p3); pos.Add(p4);
            if (positive == (axis == 1))
            {
                idx.Add(baseIdx); idx.Add(baseIdx + 1); idx.Add(baseIdx + 2);
                idx.Add(baseIdx); idx.Add(baseIdx + 2); idx.Add(baseIdx + 3);
            }
            else
            {
                idx.Add(baseIdx); idx.Add(baseIdx + 2); idx.Add(baseIdx + 1);
                idx.Add(baseIdx); idx.Add(baseIdx + 3); idx.Add(baseIdx + 2);
            }
        }

        private bool IsVoxelPresent(int x, int y, int z)
        {
            if (x < 0 || x >= _sizeX || y < 0 || y >= _sizeY || z < 0 || z >= _sizeZ) return false;
            // Оптимизация: 32 - это 2^5, используем сдвиги вместо деления
            int cx = x >> 5;
            int cy = y >> 5;
            int cz = z >> 5;
            return _chunks[cx, cy, cz].GetVoxel(x & 31, y & 31, z & 31);
        }

        private bool TryGetRemovedVoxelColor(int x, int y, int z, out uint color)
        {
            color = 0;
            if (x < 0 || x >= _sizeX || y < 0 || y >= _sizeY || z < 0 || z >= _sizeZ)
            {
                return false;
            }

            int cx = x >> 5;
            int cy = y >> 5;
            int cz = z >> 5;
            return _chunks[cx, cy, cz].TryGetRemovedVoxelColor(x & 31, y & 31, z & 31, out color);
        }

        private uint GetFaceColor(int x, int y, int z, int nx, int ny, int nz)
        {
            if (TryGetRemovedVoxelColor(nx, ny, nz, out uint color))
            {
                return color;
            }

            return DefaultSurfaceColor;
        }

        private static uint PackColor(Color color)
        {
            return ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        }

        private static Color UnpackColor(uint packed)
        {
            byte a = (byte)((packed >> 24) & 0xFF);
            byte r = (byte)((packed >> 16) & 0xFF);
            byte g = (byte)((packed >> 8) & 0xFF);
            byte b = (byte)(packed & 0xFF);
            return Color.FromArgb(a, r, g, b);
        }

        private static DiffuseMaterial BuildMaterial(uint packedColor)
        {
            var brush = new SolidColorBrush(UnpackColor(packedColor));
            brush.Freeze();
            var material = new DiffuseMaterial(brush);
            material.Freeze();
            return material;
        }
    }
}
