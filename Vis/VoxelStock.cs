using System;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using CNCSS.Data.Tools;
using CNCSS.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CNCSS.Vis
{
    /// <summary>
    /// Представляет заготовку в виде воксельной сетки, оптимизированной чанками и Greedy Meshing.
    /// </summary>
    public class VoxelStock
    {
        private readonly VoxelChunk[,,] _chunks;
        private readonly double _resolution;
        private readonly Point3D _min;
        private readonly int _sizeX, _sizeY, _sizeZ;
        private readonly int _chunkCountX, _chunkCountY, _chunkCountZ;

        private readonly Dictionary<(int, int, int), GeometryModel3D> _chunkModels = new Dictionary<(int, int, int), GeometryModel3D>();
        private readonly Model3DGroup _modelGroup = new Model3DGroup();
        private readonly DiffuseMaterial _material = new DiffuseMaterial(Brushes.Gray);

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

        public void CutCylinder(Point3D start, Point3D end, double radius, double fluteLength)
        {
            double r2 = radius * radius;
            Vector3D dir = end - start;
            double len2_xy = dir.X * dir.X + dir.Y * dir.Y;

            int minX = Math.Max(0, (int)((Math.Min(start.X, end.X) - radius - _min.X) / _resolution));
            int maxX = Math.Min(_sizeX - 1, (int)((Math.Max(start.X, end.X) + radius - _min.X) / _resolution));
            int minY = Math.Max(0, (int)((Math.Min(start.Y, end.Y) - radius - _min.Y) / _resolution));
            int maxY = Math.Min(_sizeY - 1, (int)((Math.Max(start.Y, end.Y) + radius - _min.Y) / _resolution));

            int minCX = Math.Max(0, minX / VoxelChunk.Size);
            int maxCX = Math.Min(_chunkCountX - 1, maxX / VoxelChunk.Size);
            int minCY = Math.Max(0, minY / VoxelChunk.Size);
            int maxCY = Math.Min(_chunkCountY - 1, maxY / VoxelChunk.Size);

            if (minCX > maxCX || minCY > maxCY) return;

            Parallel.For(minCX, maxCX + 1, cx =>
            {
                for (int cy = minCY; cy <= maxCY; cy++)
                {
                    for (int cz = 0; cz < _chunkCountZ; cz++)
                    {
                        var chunk = _chunks[cx, cy, cz];
                        if (chunk == null || chunk.IsEmpty) continue;

                        bool chunkModified = false;
                        int xStart = cx * VoxelChunk.Size;
                        int yStart = cy * VoxelChunk.Size;
                        int zStartChunk = cz * VoxelChunk.Size;

                        // Быстрая проверка: пересекает ли цилиндр этот чанк по вертикали
                        double chunkMinZ = _min.Z + zStartChunk * _resolution;
                        double chunkMaxZ = chunkMinZ + VoxelChunk.Size * _resolution;
                        
                        // Находим диапазон Z для текущего прохода в этом чанке
                        // (упрощенная проверка, чтобы не заходить в циклы, если инструмент выше или ниже чанка)

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
                                            chunk.SetVoxel(lx, ly, lz, false);
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

        public async Task UpdateVisualsAsync()
        {
            if (_isUpdating) return;
            _isUpdating = true;

            try
            {
                List<Task<(int, int, int, MeshGeometry3D?)>> tasks = new();
                int updatedCount = 0;

                for (int cz = 0; cz < _chunkCountZ; cz++)
                {
                    for (int cy = 0; cy < _chunkCountY; cy++)
                    {
                        for (int cx = 0; cx < _chunkCountX; cx++)
                        {
                            var chunk = _chunks[cx, cy, cz];
                            if (chunk.IsDirty)
                            {
                                var key = (cx, cy, cz);
                                chunk.IsDirty = false;

                                if (chunk.IsEmpty)
                                {
                                    tasks.Add(Task.FromResult<(int, int, int, MeshGeometry3D?)>((cx, cy, cz, null)));
                                }
                                else
                                {
                                    int localCx = cx, localCy = cy, localCz = cz;
                                    tasks.Add(Task.Run(() => (localCx, localCy, localCz, (MeshGeometry3D?)CreateChunkMesh(localCx, localCy, localCz))));
                                }
                            }
                        }
                    }
                }

                if (tasks.Count > 0)
                {
                    var results = await Task.WhenAll(tasks);
                    
                    // Оптимизация: Накапливаем изменения для пакетного обновления UI
                    var toRemove = new List<GeometryModel3D>();
                    var toAdd = new List<GeometryModel3D>();

                    foreach (var (cx, cy, cz, mesh) in results)
                    {
                        var key = (cx, cy, cz);
                        if (mesh == null)
                        {
                            if (_chunkModels.TryGetValue(key, out var model))
                            {
                                toRemove.Add(model);
                                _chunkModels.Remove(key);
                            }
                        }
                        else
                        {
                            mesh.Freeze(); 
                            if (_chunkModels.TryGetValue(key, out var existingModel))
                            {
                                existingModel.Geometry = mesh;
                            }
                            else
                            {
                                var newModel = new GeometryModel3D(mesh, _material);
                                newModel.BackMaterial = _material;
                                _chunkModels[key] = newModel;
                                toAdd.Add(newModel);
                            }
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

        private MeshGeometry3D CreateChunkMesh(int cx, int cy, int cz)
        {
            var mesh = new MeshGeometry3D();
            var positions = new Point3DCollection();
            var indices = new Int32Collection();
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

                    bool[,] mask = new bool[VoxelChunk.Size, VoxelChunk.Size];
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
                                    mask[u, v] = true;
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
                            if (mask[u, v])
                            {
                                int w, h;
                                for (w = 1; u + w < VoxelChunk.Size && mask[u + w, v]; w++) ;
                                bool done = false;
                                for (h = 1; v + h < VoxelChunk.Size; h++)
                                {
                                    for (int k = 0; k < w; k++)
                                    {
                                        if (!mask[u + k, v + h]) { done = true; break; }
                                    }
                                    if (done) break;
                                }

                                int faceU = (axis == 0 ? yBase : xBase) + u;
                                int faceV = (axis == 2 ? yBase : zBase) + v;
                                AddGreedyFace(positions, indices, axis, positive, gi, faceU, faceV, w, h);

                                for (int l = 0; l < h; l++)
                                    for (int k = 0; k < w; k++)
                                        mask[u + k, v + l] = false;
                            }
                        }
                    }
                }
            }

            mesh.Positions = positions;
            mesh.TriangleIndices = indices;
            mesh.Freeze();
            return mesh;
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
    }
}
