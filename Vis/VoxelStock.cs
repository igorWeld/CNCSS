using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using HelixToolkit.Wpf;
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
    public class VoxelStock : IStockVolume
    {
        private sealed class ColorMeshAccumulator
        {
            public Point3DCollection Positions { get; } = new Point3DCollection(256);
            public Int32Collection Indices { get; } = new Int32Collection(384);
        }

        private readonly VoxelChunk[,,] _chunks;
        private readonly double _resolution;
        private readonly Point3D _min;
        private readonly int _sizeX, _sizeY, _sizeZ;
        private readonly int _chunkCountX, _chunkCountY, _chunkCountZ;
        private readonly ReaderWriterLockSlim _voxelsLock = new ReaderWriterLockSlim();

        private readonly Dictionary<(int, int, int), Model3D> _chunkModels = new Dictionary<(int, int, int), Model3D>();
        private readonly HashSet<(int, int, int)> _currentlyVisibleChunkKeys = new();
        private readonly Dictionary<(int, int, int), Dictionary<uint, GeometryModel3D>> _chunkColorModels = new();
        private readonly HashSet<uint> _applySeenColors = new();
        private readonly List<uint> _applyStaleColors = new();
        private int _collectAllValidationTick;

        /// <summary>Стабильная ссылка для Viewport3D: меняем только дочерние chunk-группы инкрементально.</summary>
        private readonly Model3DGroup _presentationRoot = new();
        private const int ChunkMaskLength = VoxelChunk.Size * VoxelChunk.Size;
        private const uint DefaultSurfaceColorFallback = 0xFF808080;
        private readonly uint _defaultSurfaceColor;

        /// <summary>Пересборка грязных чанков — можно задействовать все ядра.</summary>
        private static readonly ParallelOptions CpuParallelMesh =
            new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) };

        /// <summary>Съём резцом ограничен по параллелизму, чтобы не выбивать из кадров UI при одновременном мешинге.</summary>
        private static readonly ParallelOptions CpuParallelCuts =
            new() { MaxDegreeOfParallelism = Math.Min(4, Math.Max(1, Environment.ProcessorCount)) };

        private readonly ConcurrentDictionary<uint, DiffuseMaterial> _materialCache = new();

        /// <summary>Подсказка грязных чанков после съёма — чтобы не сканировать всю решётку O(N) каждый кадр.</summary>
        private readonly ConcurrentDictionary<(int cx, int cy, int cz), byte> _dirtyChunkHints = new();

        private readonly object _adaptiveFocusSync = new();
        private bool _hasAdaptivePreciseRegion;
        private Point3D _adaptivePreciseMin;
        private Point3D _adaptivePreciseMax;
        private const int AdaptivePreciseNeighborChunks = 1;
        private bool _hasCameraFocus;
        private Point3D _cameraFocusPosition;
        private Vector3D _cameraFocusForward;

        [ThreadStatic] private static bool[]? s_chunkMask;
        [ThreadStatic] private static uint[]? s_chunkColorMask;
        [ThreadStatic] private static int[]? s_touchedMaskIndices;
        private volatile bool _isFrozen;

        public readonly record struct CylinderCut(Point3D Start, Point3D End, double Radius, double FluteLength, Color ToolColor);

        public Model3D MainModel => _presentationRoot;
        public bool IsCutsFrozen => _isFrozen;
        public bool IsDirty { get; set; } = true;
        public long TotalVoxelCount => (long)_sizeX * _sizeY * _sizeZ;
        public int TotalChunkCount => _chunkCountX * _chunkCountY * _chunkCountZ;
        public int DirtyChunkCount => CountDirtyChunks();
        public Point3D Min => _min;
        public Point3D Max => new Point3D(_min.X + _sizeX * _resolution, _min.Y + _sizeY * _resolution, _min.Z + _sizeZ * _resolution);
        public double Resolution => _resolution;

        public int GridDimensionX => _sizeX;
        public int GridDimensionY => _sizeY;
        public int GridDimensionZ => _sizeZ;

        /// <summary>
        /// Уплотнённая маска занятости: каждая ячейка = 1, если в блоке stride×stride×stride есть хоть один материальный воксель.
        /// </summary>
        /// <returns>Число записанных байт (nx*ny*nz).</returns>
        public int FillDownsampledOccupancyMask(Span<byte> destination, int stride, out int nx, out int ny, out int nz)
        {
            if (stride < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(stride));
            }

            nx = (_sizeX + stride - 1) / stride;
            ny = (_sizeY + stride - 1) / stride;
            nz = (_sizeZ + stride - 1) / stride;
            int need = checked(nx * ny * nz);
            if (destination.Length < need)
            {
                throw new ArgumentException($"Буфер {destination.Length} < {need}.", nameof(destination));
            }

            _voxelsLock.EnterReadLock();
            try
            {
                int w = 0;
                for (int iz = 0; iz < nz; iz++)
                {
                    int z0 = iz * stride;
                    for (int iy = 0; iy < ny; iy++)
                    {
                        int y0 = iy * stride;
                        for (int ix = 0; ix < nx; ix++)
                        {
                            int x0 = ix * stride;
                            byte cell = 0;
                            bool found = false;
                            for (int dz = 0; dz < stride && z0 + dz < _sizeZ && !found; dz++)
                            {
                                for (int dy = 0; dy < stride && y0 + dy < _sizeY && !found; dy++)
                                {
                                    for (int dx = 0; dx < stride && x0 + dx < _sizeX; dx++)
                                    {
                                        if (IsVoxelPresentUnchecked(x0 + dx, y0 + dy, z0 + dz))
                                        {
                                            cell = 1;
                                            found = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            destination[w++] = cell;
                        }
                    }
                }
            }
            finally
            {
                _voxelsLock.ExitReadLock();
            }

            return need;
        }

        /// <summary>
        /// Прямоугольный объём: все ячейки заняты (без маски формы).
        /// <paramref name="defaultSurfaceColor"/> — цвет из конструктора заготовки; иначе светло-серый.
        /// </summary>
        public VoxelStock(double width, double depth, double height, double resolution, Point3D center, double maxZ, Color? defaultSurfaceColor = null)
        {
            _defaultSurfaceColor = defaultSurfaceColor.HasValue ? PackColor(defaultSurfaceColor.Value) : DefaultSurfaceColorFallback;
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
            _presentationRoot.Children.Clear();
        }

        /// <summary>
        /// Заготовка из плотной маски (1 = материал, 0 = пусто), в т.ч. из <see cref="StockSimulationCoordinator"/>.
        /// Индексация: ix + sx * (iy + sy * iz), как в <c>CNCSS.GpuVerification</c>.
        /// <paramref name="defaultSurfaceColor"/> — цвет из конструктора заготовки.
        /// </summary>
        public static VoxelStock FromGpuOccupancyMask(
            double width,
            double depth,
            double height,
            double resolutionMm,
            Point3D center,
            double maxZ,
            uint[] occupancyGpu,
            Color? defaultSurfaceColor = null)
        {
            var stock = new VoxelStock(width, depth, height, resolutionMm, center, maxZ, defaultSurfaceColor);
            int cells = checked(stock._sizeX * stock._sizeY * stock._sizeZ);
            if (occupancyGpu.Length != cells)
            {
                throw new ArgumentException($"GPU mask size {occupancyGpu.Length} != grid {cells}.", nameof(occupancyGpu));
            }

            stock.ApplyGpuOccupancyMask(occupancyGpu);
            return stock;
        }

        /// <summary>
        /// Применение маски с GPU: параллельно по чанкам 32³ (разные чанки не делят воксели — без гонок).
        /// </summary>
        private void ApplyGpuOccupancyMask(uint[] occ)
        {
            _voxelsLock.EnterWriteLock();
            try
            {
                int sx = _sizeX;
                int sy = _sizeY;
                int chunkPlane = _chunkCountX * _chunkCountY;
                int totalChunks = chunkPlane * _chunkCountZ;

                void ProcessOneChunk(int chunkIdx)
                {
                    int cz = chunkIdx / chunkPlane;
                    int rem = chunkIdx % chunkPlane;
                    int cy = rem / _chunkCountX;
                    int cx = rem % _chunkCountX;

                    int xBase = cx * VoxelChunk.Size;
                    int yBase = cy * VoxelChunk.Size;
                    int zBase = cz * VoxelChunk.Size;

                    bool modified = false;
                    for (int lz = 0; lz < VoxelChunk.Size; lz++)
                    {
                        int gz = zBase + lz;
                        if (gz >= _sizeZ)
                        {
                            break;
                        }

                        for (int ly = 0; ly < VoxelChunk.Size; ly++)
                        {
                            int gy = yBase + ly;
                            if (gy >= _sizeY)
                            {
                                break;
                            }

                            for (int lx = 0; lx < VoxelChunk.Size; lx++)
                            {
                                int gx = xBase + lx;
                                if (gx >= _sizeX)
                                {
                                    break;
                                }

                                int i = gx + sx * (gy + sy * gz);
                                if (occ[i] != 0)
                                {
                                    continue;
                                }

                                if (_chunks[cx, cy, cz].ClearVoxel(lx, ly, lz, _defaultSurfaceColor))
                                {
                                    modified = true;
                                }
                            }
                        }
                    }

                    if (modified)
                    {
                        HintChunkDirty(cx, cy, cz);
                    }
                }

                if (totalChunks >= 8)
                {
                    Parallel.For(0, totalChunks, CpuParallelMesh, ProcessOneChunk);
                }
                else
                {
                    for (int c = 0; c < totalChunks; c++)
                    {
                        ProcessOneChunk(c);
                    }
                }
            }
            finally
            {
                _voxelsLock.ExitWriteLock();
            }

            IsDirty = true;
        }

        /// <inheritdoc />
        public void AttachBoundingSolidPlaceholder(Color diffuse) { }

        private void HintChunkDirty(int cx, int cy, int cz)
        {
            _dirtyChunkHints.TryAdd((cx, cy, cz), 0);
            IsDirty = true;
        }

        private void HintChunkAndNeighborsDirty(int cx, int cy, int cz)
        {
            // Mesh visibility depends on all adjacent cells, including diagonal edge/corner cases.
            // Mark the full 3x3x3 neighborhood so a newly exposed face is rebuilt from every camera angle.
            for (int dz = -1; dz <= 1; dz++)
            {
                int nz = cz + dz;
                if (nz < 0 || nz >= _chunkCountZ)
                {
                    continue;
                }

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = cy + dy;
                    if (ny < 0 || ny >= _chunkCountY)
                    {
                        continue;
                    }

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx;
                        if (nx < 0 || nx >= _chunkCountX)
                        {
                            continue;
                        }

                        HintChunkDirty(nx, ny, nz);
                    }
                }
            }
        }

        public void CutCylinder(Point3D start, Point3D end, double radius, double fluteLength, Color toolColor)
        {
            if (_isFrozen)
            {
                return;
            }

            _voxelsLock.EnterWriteLock();
            try
            {
                CutCylinderCore(start, end, radius, fluteLength, PackColor(toolColor), useParallelChunks: true);
            }
            finally
            {
                _voxelsLock.ExitWriteLock();
            }

            // После съёма (вне write-lock): зона контакта для приоритета пересборки меша.
            RefreshAdaptiveContactRegionAfterCut(start, end, radius, fluteLength);
        }

        public void CutCylinders(IReadOnlyList<CylinderCut> cuts, Action<int, int>? progress = null)
        {
            if (_isFrozen || cuts.Count == 0)
            {
                return;
            }

            _voxelsLock.EnterWriteLock();
            try
            {
                var modifiedChunks = new HashSet<(int cx, int cy, int cz)>();
                int reportEvery = Math.Max(1, cuts.Count / 100);
                for (int i = 0; i < cuts.Count; i++)
                {
                    var cut = cuts[i];
                    CutCylinderCore(
                        cut.Start,
                        cut.End,
                        cut.Radius,
                        cut.FluteLength,
                        PackColor(cut.ToolColor),
                        useParallelChunks: false,
                        modifiedChunks);
                    if ((i + 1) % reportEvery == 0 || i + 1 == cuts.Count)
                    {
                        progress?.Invoke(i + 1, cuts.Count);
                    }
                }

                foreach (var (cx, cy, cz) in modifiedChunks)
                {
                    HintChunkAndNeighborsDirty(cx, cy, cz);
                }
            }
            finally
            {
                _voxelsLock.ExitWriteLock();
            }
        }

        private void CutCylinderCore(
            Point3D start,
            Point3D end,
            double radius,
            double fluteLength,
            uint toolColorPacked,
            bool useParallelChunks,
            HashSet<(int cx, int cy, int cz)>? batchModifiedChunks = null)
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

            void ProcessChunkColumn(int cx, Action<int, int, int> markModifiedChunk)
            {
                int chunkX0 = cx * VoxelChunk.Size;
                int chunkX1 = Math.Min(_sizeX - 1, chunkX0 + VoxelChunk.Size - 1);
                int xStart = Math.Max(minX, chunkX0);
                int xEnd = Math.Min(maxX, chunkX1);
                if (xStart > xEnd)
                {
                    return;
                }

                for (int gx = xStart; gx <= xEnd; gx++)
                {
                    double px = _min.X + gx * _resolution;
                    int lx = gx & 31;

                    for (int gy = minY; gy <= maxY; gy++)
                    {
                        double py = _min.Y + gy * _resolution;
                        double t = len2_xy < ProjectConstants.EPSILON
                            ? 0
                            : Math.Max(0, Math.Min(1, ((px - start.X) * dir.X + (py - start.Y) * dir.Y) / (len2_xy + ProjectConstants.SMALL_EPSILON)));
                        double dx = px - (start.X + t * dir.X);
                        double dy = py - (start.Y + t * dir.Y);
                        if (dx * dx + dy * dy > r2)
                        {
                            continue;
                        }

                        double currentTipZ = start.Z + t * dir.Z;
                        double currentTopZ = currentTipZ + fluteLength;
                        int gzStart = Math.Max(minZ, (int)((currentTipZ - _min.Z) / _resolution));
                        int gzEnd = Math.Min(maxZ, (int)((currentTopZ - _min.Z) / _resolution));
                        if (gzStart > gzEnd)
                        {
                            continue;
                        }

                        int ly = gy & 31;
                        for (int gz = gzStart; gz <= gzEnd; gz++)
                        {
                            int cz = gz >> 5;
                            int cy = gy >> 5;
                            var chunk = _chunks[cx, cy, cz];
                            if (chunk.IsEmpty)
                            {
                                continue;
                            }

                            int lz = gz & 31;
                            if (chunk.ClearVoxel(lx, ly, lz, toolColorPacked))
                            {
                                markModifiedChunk(cx, cy, cz);
                            }
                        }
                    }
                }
            }

            if (useParallelChunks && maxCX > minCX)
            {
                var modifiedMarks = new ConcurrentDictionary<(int cx, int cy, int cz), byte>();
                Parallel.For(minCX, maxCX + 1, CpuParallelCuts, cx =>
                {
                    ProcessChunkColumn(cx, (mx, my, mz) => modifiedMarks.TryAdd((mx, my, mz), 0));
                });

                foreach (var key in modifiedMarks.Keys)
                {
                    if (batchModifiedChunks != null)
                    {
                        batchModifiedChunks.Add(key);
                    }
                    else
                    {
                        HintChunkAndNeighborsDirty(key.cx, key.cy, key.cz);
                    }
                }
            }
            else
            {
                for (int cx = minCX; cx <= maxCX; cx++)
                {
                    ProcessChunkColumn(cx, (mx, my, mz) =>
                    {
                        if (batchModifiedChunks != null)
                        {
                            batchModifiedChunks.Add((mx, my, mz));
                        }
                        else
                        {
                            HintChunkAndNeighborsDirty(mx, my, mz);
                        }
                    });
                }
            }
        }

        public void FreezeModel()
        {
            _isFrozen = true;
            IsDirty = false;
        }

        public void ClearBox(Point3D min, Point3D max)
        {
            if (_isFrozen)
            {
                return;
            }

            _voxelsLock.EnterWriteLock();
            try
            {
                int minX = Math.Max(0, (int)((min.X - _min.X) / _resolution));
                int maxX = Math.Min(_sizeX - 1, (int)((max.X - _min.X) / _resolution));
                int minY = Math.Max(0, (int)((min.Y - _min.Y) / _resolution));
                int maxY = Math.Min(_sizeY - 1, (int)((max.Y - _min.Y) / _resolution));
                int minZ = Math.Max(0, (int)((min.Z - _min.Z) / _resolution));
                int maxZ = Math.Min(_sizeZ - 1, (int)((max.Z - _min.Z) / _resolution));

                if (minX > maxX || minY > maxY || minZ > maxZ)
                {
                    return;
                }

                int minCX = minX / VoxelChunk.Size;
                int maxCX = maxX / VoxelChunk.Size;
                int minCY = minY / VoxelChunk.Size;
                int maxCY = maxY / VoxelChunk.Size;
                int minCZ = minZ / VoxelChunk.Size;
                int maxCZ = maxZ / VoxelChunk.Size;

                for (int cx = minCX; cx <= maxCX; cx++)
                {
                    for (int cy = minCY; cy <= maxCY; cy++)
                    {
                        for (int cz = minCZ; cz <= maxCZ; cz++)
                        {
                            var chunk = _chunks[cx, cy, cz];
                            bool changed = false;
                            int xStart = cx * VoxelChunk.Size;
                            int yStart = cy * VoxelChunk.Size;
                            int zStart = cz * VoxelChunk.Size;

                            for (int lx = 0; lx < VoxelChunk.Size; lx++)
                            {
                                int gx = xStart + lx;
                                if (gx < minX || gx > maxX) continue;
                                for (int ly = 0; ly < VoxelChunk.Size; ly++)
                                {
                                    int gy = yStart + ly;
                                    if (gy < minY || gy > maxY) continue;
                                    for (int lz = 0; lz < VoxelChunk.Size; lz++)
                                    {
                                        int gz = zStart + lz;
                                        if (gz < minZ || gz > maxZ) continue;
                                        if (chunk.GetVoxel(lx, ly, lz))
                                        {
                                            chunk.SetVoxel(lx, ly, lz, false, _defaultSurfaceColor);
                                            changed = true;
                                        }
                                    }
                                }
                            }

                            if (changed)
                            {
                                HintChunkDirty(cx, cy, cz);
                            }
                        }
                    }
                }
            }
            finally
            {
                _voxelsLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Адаптивная зона точной пересборки: совпадает с предстоящим контактом режущей части с материалом.
        /// В этой зоне грязные чанки обрабатываются в первую очередь при ограниченном бюджете кадра.
        /// </summary>
        public void SetAdaptivePreciseContactRegion(
            Point3D start,
            Point3D end,
            double radius,
            double fluteLength,
            bool hasUpcomingContact)
        {
            lock (_adaptiveFocusSync)
            {
                if (!hasUpcomingContact)
                {
                    _hasAdaptivePreciseRegion = false;
                    return;
                }

                double margin = Math.Max(_resolution, radius * 0.25);
                _adaptivePreciseMin = new Point3D(
                    Math.Min(start.X, end.X) - radius - margin,
                    Math.Min(start.Y, end.Y) - radius - margin,
                    Math.Min(start.Z, end.Z) - margin);
                _adaptivePreciseMax = new Point3D(
                    Math.Max(start.X, end.X) + radius + margin,
                    Math.Max(start.Y, end.Y) + radius + margin,
                    Math.Max(start.Z, end.Z) + fluteLength + margin);
                _hasAdaptivePreciseRegion = true;
            }
        }

        private void RefreshAdaptiveContactRegionAfterCut(Point3D start, Point3D end, double radius, double fluteLength)
        {
            double triggerDistance = Math.Max(_resolution * 4.0, radius * 0.35);
            bool near = HasMaterialNearSweep(start, end, radius, fluteLength, triggerDistance);
            SetAdaptivePreciseContactRegion(start, end, radius, fluteLength, near);
        }

        private bool IsChunkInAdaptivePreciseRegion(int cx, int cy, int cz)
        {
            Point3D min;
            Point3D max;
            bool hasRegion;
            lock (_adaptiveFocusSync)
            {
                hasRegion = _hasAdaptivePreciseRegion;
                min = _adaptivePreciseMin;
                max = _adaptivePreciseMax;
            }

            if (!hasRegion)
            {
                return true;
            }

            int minCx = Math.Max(0, (int)((min.X - _min.X) / (_resolution * VoxelChunk.Size)) - AdaptivePreciseNeighborChunks);
            int maxCx = Math.Min(_chunkCountX - 1, (int)((max.X - _min.X) / (_resolution * VoxelChunk.Size)) + AdaptivePreciseNeighborChunks);
            int minCy = Math.Max(0, (int)((min.Y - _min.Y) / (_resolution * VoxelChunk.Size)) - AdaptivePreciseNeighborChunks);
            int maxCy = Math.Min(_chunkCountY - 1, (int)((max.Y - _min.Y) / (_resolution * VoxelChunk.Size)) + AdaptivePreciseNeighborChunks);
            int minCz = Math.Max(0, (int)((min.Z - _min.Z) / (_resolution * VoxelChunk.Size)) - AdaptivePreciseNeighborChunks);
            int maxCz = Math.Min(_chunkCountZ - 1, (int)((max.Z - _min.Z) / (_resolution * VoxelChunk.Size)) + AdaptivePreciseNeighborChunks);

            return cx >= minCx && cx <= maxCx &&
                   cy >= minCy && cy <= maxCy &&
                   cz >= minCz && cz <= maxCz;
        }

        /// <summary>
        /// Подсказка от камеры: в первую очередь пересобираем чанки, которые пользователь видит сейчас.
        /// На геометрию съёма не влияет, только на порядок визуальной дорисовки.
        /// </summary>
        public void SetCameraFocus(Point3D position, Vector3D forward)
        {
            lock (_adaptiveFocusSync)
            {
                if (forward.LengthSquared <= ProjectConstants.EPSILON)
                {
                    _hasCameraFocus = false;
                    return;
                }

                forward.Normalize();
                _cameraFocusPosition = position;
                _cameraFocusForward = forward;
                _hasCameraFocus = true;
            }
        }

        /// <summary>
        /// Показ только чанков в видимой области камеры.
        /// Вызывается на UI-потоке при изменении ракурса (поворот/зум).
        /// </summary>
        public bool UpdateVisibleChunksForCamera(
            Point3D cameraPosition,
            Vector3D lookDirection,
            Vector3D upDirection,
            double verticalFovDegrees,
            double aspectRatio)
        {
            // Ручной camera-culling отключён: все построенные чанки остаются в сцене,
            // а отсечение делается на уровне WPF/Helix. Здесь оставлен no-op, чтобы
            // не выполнять тяжёлую синхронизацию Children на каждый кадр рендера.
            return false;
        }

        private bool IsChunkInCameraFocusRegion(int cx, int cy, int cz)
        {
            Point3D cameraPos;
            Vector3D cameraForward;
            bool hasFocus;
            lock (_adaptiveFocusSync)
            {
                hasFocus = _hasCameraFocus;
                cameraPos = _cameraFocusPosition;
                cameraForward = _cameraFocusForward;
            }

            if (!hasFocus)
            {
                return true;
            }

            double x0 = _min.X + cx * VoxelChunk.Size * _resolution;
            double y0 = _min.Y + cy * VoxelChunk.Size * _resolution;
            double z0 = _min.Z + cz * VoxelChunk.Size * _resolution;
            double x1 = _min.X + Math.Min(_sizeX, (cx + 1) * VoxelChunk.Size) * _resolution;
            double y1 = _min.Y + Math.Min(_sizeY, (cy + 1) * VoxelChunk.Size) * _resolution;
            double z1 = _min.Z + Math.Min(_sizeZ, (cz + 1) * VoxelChunk.Size) * _resolution;
            Point3D center = new Point3D((x0 + x1) * 0.5, (y0 + y1) * 0.5, (z0 + z1) * 0.5);

            Vector3D toChunk = center - cameraPos;
            double dist2 = toChunk.LengthSquared;
            if (dist2 <= ProjectConstants.EPSILON)
            {
                return true;
            }

            double stockDiag = Math.Sqrt(_sizeX * _sizeX + _sizeY * _sizeY + _sizeZ * _sizeZ) * _resolution;
            double focusRadius = Math.Max(50.0, stockDiag * 0.8);
            if (dist2 > focusRadius * focusRadius)
            {
                return false;
            }

            toChunk.Normalize();
            return Vector3D.DotProduct(toChunk, cameraForward) >= -0.2;
        }

        public bool HasMaterialNearSweep(Point3D start, Point3D end, double radius, double fluteLength, double triggerDistance)
        {
            Vector3D dir = end - start;
            double len2xy = dir.X * dir.X + dir.Y * dir.Y;
            double extra = Math.Max(triggerDistance, _resolution);

            int minX = Math.Max(0, (int)((Math.Min(start.X, end.X) - radius - extra - _min.X) / _resolution));
            int maxX = Math.Min(_sizeX - 1, (int)((Math.Max(start.X, end.X) + radius + extra - _min.X) / _resolution));
            int minY = Math.Max(0, (int)((Math.Min(start.Y, end.Y) - radius - extra - _min.Y) / _resolution));
            int maxY = Math.Min(_sizeY - 1, (int)((Math.Max(start.Y, end.Y) + radius + extra - _min.Y) / _resolution));
            int minZ = Math.Max(0, (int)((Math.Min(start.Z, end.Z) - extra - _min.Z) / _resolution));
            int maxZ = Math.Min(_sizeZ - 1, (int)((Math.Max(start.Z, end.Z) + fluteLength + extra - _min.Z) / _resolution));

            if (minX > maxX || minY > maxY || minZ > maxZ)
            {
                return false;
            }

            _voxelsLock.EnterReadLock();
            try
            {
                for (int gx = minX; gx <= maxX; gx++)
                {
                    double px = _min.X + gx * _resolution;
                    for (int gy = minY; gy <= maxY; gy++)
                    {
                        double py = _min.Y + gy * _resolution;
                        double t = len2xy < ProjectConstants.EPSILON
                            ? 0
                            : Math.Max(0, Math.Min(1, ((px - start.X) * dir.X + (py - start.Y) * dir.Y) / (len2xy + ProjectConstants.SMALL_EPSILON)));
                        double dx = px - (start.X + t * dir.X);
                        double dy = py - (start.Y + t * dir.Y);
                        if (dx * dx + dy * dy > (radius + extra) * (radius + extra))
                        {
                            continue;
                        }

                        double currentTipZ = start.Z + t * dir.Z;
                        int gzStart = Math.Max(minZ, (int)((currentTipZ - extra - _min.Z) / _resolution));
                        int gzEnd = Math.Min(maxZ, (int)((currentTipZ + fluteLength + extra - _min.Z) / _resolution));

                        for (int gz = gzStart; gz <= gzEnd; gz++)
                        {
                            if (IsVoxelPresentUnchecked(gx, gy, gz))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            finally
            {
                _voxelsLock.ExitReadLock();
            }

            return false;
        }

        /// <summary>
        /// Ленивая подгрузка отключена: всегда пересобираем все грязные чанки за один проход.
        /// </summary>
        public Task UpdateVisualsAsync() => UpdateVisualsCoreAsync(int.MaxValue);

        public Task UpdateAllVisualsAsync() => UpdateVisualsCoreAsync(int.MaxValue);

        public Task UpdateVisualsBatchAsync(int maxChunks) => UpdateVisualsCoreAsync(Math.Max(1, maxChunks));

        private async Task UpdateVisualsCoreAsync(int? chunkBudgetOverride)
        {
            int chunkBudget = chunkBudgetOverride ?? int.MaxValue;

            var dirty = CollectDirtyChunks(chunkBudget);
            if (dirty.Count == 0)
            {
                IsDirty = false;
                return;
            }

            var results = new (int cx, int cy, int cz, List<(uint color, MeshGeometry3D mesh)>? meshes)[dirty.Count];

            await Task.Run(() =>
            {
                if (dirty.Count >= 6)
                {
                    Parallel.For(0, dirty.Count, CpuParallelMesh, BuildOne);
                }
                else
                {
                    for (int i = 0; i < dirty.Count; i++)
                    {
                        BuildOne(i);
                    }
                }

                void BuildOne(int i)
                {
                    var item = dirty[i];
                    if (item.isEmpty)
                    {
                        results[i] = (item.cx, item.cy, item.cz, null);
                        return;
                    }

                    List<(uint color, MeshGeometry3D mesh)> meshes = CreateChunkMeshes(item.cx, item.cy, item.cz);
                    results[i] = (item.cx, item.cy, item.cz, meshes);
                }
            }).ConfigureAwait(true);

            ApplyChunkMeshesToScene(results);

            // Во время фоновой сборки меша инструмент может успеть сделать новый рез.
            // Важно отфильтровать устаревшие hints: иначе IsDirty может "залипнуть" в true
            // и вызвать бесконечные циклы пересборки.
            IsDirty = HasPendingDirtyHints() || HasAnyDirtyChunk();
        }

        /// <summary>Снимает Dirty только с первых maxChunks чанков — остальные дорисовываются на следующих кадрах (без длинной блокировки UI).</summary>
        private List<(int cx, int cy, int cz, bool isEmpty)> CollectDirtyChunks(int maxChunks)
        {
            if (maxChunks == int.MaxValue)
            {
                return CollectAllDirtyChunks();
            }

            var dirty = new List<(int cx, int cy, int cz, bool isEmpty)>(Math.Min(maxChunks, 64));

            static int CompareChunks((int cx, int cy, int cz) a, (int cx, int cy, int cz) b)
            {
                int cmp = a.cx.CompareTo(b.cx);
                if (cmp != 0)
                {
                    return cmp;
                }

                cmp = a.cy.CompareTo(b.cy);
                return cmp != 0 ? cmp : a.cz.CompareTo(b.cz);
            }

            void TryEnqueueChunk(int cx, int cy, int cz, bool preciseOnly, bool cameraOnly)
            {
                if (dirty.Count >= maxChunks)
                {
                    return;
                }

                var chunk = _chunks[cx, cy, cz];
                if (!chunk.IsDirty)
                {
                    _dirtyChunkHints.TryRemove((cx, cy, cz), out _);
                    return;
                }

                if (preciseOnly && !IsChunkInAdaptivePreciseRegion(cx, cy, cz))
                {
                    return;
                }

                if (cameraOnly && !IsChunkInCameraFocusRegion(cx, cy, cz))
                {
                    return;
                }

                bool noVisibleFaces = chunk.IsEmpty || IsFullySurroundedSolidChunk(cx, cy, cz);
                chunk.IsDirty = false;
                _dirtyChunkHints.TryRemove((cx, cy, cz), out _);
                dirty.Add((cx, cy, cz, noVisibleFaces));
            }

            // 1) Подсказки от последних резов — сначала зона контакта, затем остальные.
            if (!_dirtyChunkHints.IsEmpty)
            {
                var keys = _dirtyChunkHints.Keys.ToArray();
                Array.Sort(keys, CompareChunks);

                foreach (var k in keys)
                {
                    TryEnqueueChunk(k.cx, k.cy, k.cz, preciseOnly: true, cameraOnly: true);
                    if (dirty.Count >= maxChunks)
                    {
                        return dirty;
                    }
                }

                foreach (var k in keys)
                {
                    TryEnqueueChunk(k.cx, k.cy, k.cz, preciseOnly: false, cameraOnly: false);
                    if (dirty.Count >= maxChunks)
                    {
                        return dirty;
                    }
                }
            }

            // 2) Линейное сканирование: контактная зона в направлении камеры.
            for (int cz = 0; cz < _chunkCountZ && dirty.Count < maxChunks; cz++)
            {
                for (int cy = 0; cy < _chunkCountY && dirty.Count < maxChunks; cy++)
                {
                    for (int cx = 0; cx < _chunkCountX && dirty.Count < maxChunks; cx++)
                    {
                        if (!_chunks[cx, cy, cz].IsDirty)
                        {
                            continue;
                        }

                        TryEnqueueChunk(cx, cy, cz, preciseOnly: true, cameraOnly: true);
                    }
                }
            }

            // 3) Контактная зона вне поля камеры.
            for (int cz = 0; cz < _chunkCountZ && dirty.Count < maxChunks; cz++)
            {
                for (int cy = 0; cy < _chunkCountY && dirty.Count < maxChunks; cy++)
                {
                    for (int cx = 0; cx < _chunkCountX && dirty.Count < maxChunks; cx++)
                    {
                        if (!_chunks[cx, cy, cz].IsDirty)
                        {
                            continue;
                        }

                        TryEnqueueChunk(cx, cy, cz, preciseOnly: true, cameraOnly: false);
                    }
                }
            }

            // 4) Остальные грязные чанки — сначала видимые камерой.
            for (int cz = 0; cz < _chunkCountZ && dirty.Count < maxChunks; cz++)
            {
                for (int cy = 0; cy < _chunkCountY && dirty.Count < maxChunks; cy++)
                {
                    for (int cx = 0; cx < _chunkCountX && dirty.Count < maxChunks; cx++)
                    {
                        if (!_chunks[cx, cy, cz].IsDirty)
                        {
                            continue;
                        }

                        TryEnqueueChunk(cx, cy, cz, preciseOnly: false, cameraOnly: true);
                    }
                }
            }

            // 5) Остальные грязные чанки — добор бюджета.
            for (int cz = 0; cz < _chunkCountZ && dirty.Count < maxChunks; cz++)
            {
                for (int cy = 0; cy < _chunkCountY && dirty.Count < maxChunks; cy++)
                {
                    for (int cx = 0; cx < _chunkCountX && dirty.Count < maxChunks; cx++)
                    {
                        if (!_chunks[cx, cy, cz].IsDirty)
                        {
                            continue;
                        }

                        TryEnqueueChunk(cx, cy, cz, preciseOnly: false, cameraOnly: false);
                    }
                }
            }

            return dirty;
        }

        /// <summary>
        /// Быстрый полный проход без приоритезации: используется, когда ленивый режим отключён
        /// и нужно одним кадром пересобрать все «грязные» чанки.
        /// </summary>
        private List<(int cx, int cy, int cz, bool isEmpty)> CollectAllDirtyChunks()
        {
            var dirty = new List<(int cx, int cy, int cz, bool isEmpty)>(Math.Max(16, _dirtyChunkHints.Count));

            if (!_dirtyChunkHints.IsEmpty)
            {
                foreach (var key in _dirtyChunkHints.Keys)
                {
                    if (key.cx < 0 || key.cx >= _chunkCountX ||
                        key.cy < 0 || key.cy >= _chunkCountY ||
                        key.cz < 0 || key.cz >= _chunkCountZ)
                    {
                        _dirtyChunkHints.TryRemove(key, out _);
                        continue;
                    }

                    var chunk = _chunks[key.cx, key.cy, key.cz];
                    if (!chunk.IsDirty)
                    {
                        _dirtyChunkHints.TryRemove(key, out _);
                        continue;
                    }

                    bool noVisibleFaces = chunk.IsEmpty || IsFullySurroundedSolidChunk(key.cx, key.cy, key.cz);
                    chunk.IsDirty = false;
                    _dirtyChunkHints.TryRemove(key, out _);
                    dirty.Add((key.cx, key.cy, key.cz, noVisibleFaces));
                }
            }

            // Полный проход: всегда добираем оставшиеся грязные чанки (иначе IsDirty «залипает» и UI крутит пересборку бесконечно).
            _ = _collectAllValidationTick++;

            for (int cz = 0; cz < _chunkCountZ; cz++)
            {
                for (int cy = 0; cy < _chunkCountY; cy++)
                {
                    for (int cx = 0; cx < _chunkCountX; cx++)
                    {
                        var chunk = _chunks[cx, cy, cz];
                        if (!chunk.IsDirty)
                        {
                            continue;
                        }

                        bool noVisibleFaces = chunk.IsEmpty || IsFullySurroundedSolidChunk(cx, cy, cz);
                        chunk.IsDirty = false;
                        _dirtyChunkHints.TryRemove((cx, cy, cz), out _);
                        dirty.Add((cx, cy, cz, noVisibleFaces));
                    }
                }
            }

            return dirty;
        }

        private bool IsFullySurroundedSolidChunk(int cx, int cy, int cz)
        {
            if (cx <= 0 || cy <= 0 || cz <= 0 ||
                cx + 1 >= _chunkCountX || cy + 1 >= _chunkCountY || cz + 1 >= _chunkCountZ)
            {
                return false;
            }

            return _chunks[cx, cy, cz].IsFull &&
                   _chunks[cx - 1, cy, cz].IsFull &&
                   _chunks[cx + 1, cy, cz].IsFull &&
                   _chunks[cx, cy - 1, cz].IsFull &&
                   _chunks[cx, cy + 1, cz].IsFull &&
                   _chunks[cx, cy, cz - 1].IsFull &&
                   _chunks[cx, cy, cz + 1].IsFull;
        }

        /// <summary>Инкрементально обновляет только изменённые chunk-группы без полной пересборки корня сцены.</summary>
        private void ApplyChunkMeshesToScene(
            (int cx, int cy, int cz, List<(uint color, MeshGeometry3D mesh)>? meshes)[] results)
        {
            foreach (var tuple in results)
            {
                int cx = tuple.cx;
                int cy = tuple.cy;
                int cz = tuple.cz;
                List<(uint color, MeshGeometry3D mesh)>? meshes = tuple.meshes;
                var key = (cx, cy, cz);

                if (meshes == null || meshes.Count == 0)
                {
                    if (_chunkModels.TryGetValue(key, out Model3D? existing))
                    {
                        _presentationRoot.Children.Remove(existing);
                        _chunkModels.Remove(key);
                        _currentlyVisibleChunkKeys.Remove(key);
                        _chunkColorModels.Remove(key);
                    }
                    continue;
                }

                if (!_chunkModels.TryGetValue(key, out Model3D? modelNode) || modelNode is not Model3DGroup modelGroup)
                {
                    modelGroup = new Model3DGroup();
                    _chunkModels[key] = modelGroup;
                }

                if (!_chunkColorModels.TryGetValue(key, out Dictionary<uint, GeometryModel3D>? colorModels))
                {
                    colorModels = new Dictionary<uint, GeometryModel3D>(4);
                    _chunkColorModels[key] = colorModels;
                }

                _applySeenColors.Clear();
                foreach (var (color, mesh) in meshes)
                {
                    _applySeenColors.Add(color);
                    DiffuseMaterial material = GetOrCreateMaterial(color);
                    if (!colorModels.TryGetValue(color, out GeometryModel3D? gm))
                    {
                        gm = new GeometryModel3D(mesh, material) { BackMaterial = material };
                        colorModels[color] = gm;
                        modelGroup.Children.Add(gm);
                    }
                    else
                    {
                        gm.Geometry = mesh;
                        if (!ReferenceEquals(gm.Material, material))
                        {
                            gm.Material = material;
                            gm.BackMaterial = material;
                        }
                    }
                }

                if (colorModels.Count > _applySeenColors.Count)
                {
                    _applyStaleColors.Clear();
                    foreach (uint color in colorModels.Keys)
                    {
                        if (!_applySeenColors.Contains(color))
                        {
                            _applyStaleColors.Add(color);
                        }
                    }

                    for (int i = 0; i < _applyStaleColors.Count; i++)
                    {
                        uint c = _applyStaleColors[i];
                        if (colorModels.TryGetValue(c, out GeometryModel3D? gm))
                        {
                            modelGroup.Children.Remove(gm);
                            colorModels.Remove(c);
                        }
                    }
                }

                if (!_currentlyVisibleChunkKeys.Contains(key))
                {
                    _presentationRoot.Children.Add(modelGroup);
                    _currentlyVisibleChunkKeys.Add(key);
                }
            }
        }

        private bool HasAnyDirtyChunk()
        {
            for (int cz = 0; cz < _chunkCountZ; cz++)
            {
                for (int cy = 0; cy < _chunkCountY; cy++)
                {
                    for (int cx = 0; cx < _chunkCountX; cx++)
                    {
                        if (_chunks[cx, cy, cz].IsDirty)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool HasPendingDirtyHints()
        {
            if (_dirtyChunkHints.IsEmpty)
            {
                return false;
            }

            bool hasDirty = false;
            foreach (var key in _dirtyChunkHints.Keys)
            {
                if (key.cx < 0 || key.cx >= _chunkCountX ||
                    key.cy < 0 || key.cy >= _chunkCountY ||
                    key.cz < 0 || key.cz >= _chunkCountZ)
                {
                    _dirtyChunkHints.TryRemove(key, out _);
                    continue;
                }

                if (_chunks[key.cx, key.cy, key.cz].IsDirty)
                {
                    hasDirty = true;
                }
                else
                {
                    // stale hint
                    _dirtyChunkHints.TryRemove(key, out _);
                }
            }

            return hasDirty;
        }

        private int CountDirtyChunks()
        {
            int count = 0;
            for (int cz = 0; cz < _chunkCountZ; cz++)
            {
                for (int cy = 0; cy < _chunkCountY; cy++)
                {
                    for (int cx = 0; cx < _chunkCountX; cx++)
                    {
                        if (_chunks[cx, cy, cz].IsDirty)
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        private List<(uint color, MeshGeometry3D mesh)> CreateChunkMeshes(int cx, int cy, int cz)
        {
            _voxelsLock.EnterReadLock();
            try
            {
            var accumulators = new Dictionary<uint, ColorMeshAccumulator>(4);
            double res = _resolution;

            int xBase = cx * VoxelChunk.Size;
            int yBase = cy * VoxelChunk.Size;
            int zBase = cz * VoxelChunk.Size;
            int xCount = Math.Min(VoxelChunk.Size, _sizeX - xBase);
            int yCount = Math.Min(VoxelChunk.Size, _sizeY - yBase);
            int zCount = Math.Min(VoxelChunk.Size, _sizeZ - zBase);

            for (int d = 0; d < 6; d++)
            {
                int axis = d / 2;
                bool positive = d % 2 == 1;
                int iLimit = axis == 0 ? xCount : axis == 1 ? yCount : zCount;
                int uLimit = axis == 0 ? yCount : xCount;
                int vLimit = axis == 2 ? yCount : zCount;

                for (int i = 0; i < iLimit; i++)
                {
                    int gi = (axis == 0 ? xBase : axis == 1 ? yBase : zBase) + i;

                    bool[] mask = s_chunkMask ??= new bool[ChunkMaskLength];
                    uint[] colorMask = s_chunkColorMask ??= new uint[ChunkMaskLength];
                    int[] touched = s_touchedMaskIndices ??= new int[ChunkMaskLength];
                    int touchedCount = 0;
                    bool hasAny = false;

                    for (int v = 0; v < vLimit; v++)
                    {
                        for (int u = 0; u < uLimit; u++)
                        {
                            int lx = axis == 0 ? i : (axis == 1 ? u : u);
                            int ly = axis == 0 ? u : (axis == 1 ? i : v);
                            int lz = axis == 0 ? v : (axis == 1 ? v : i);

                            int gx = xBase + lx;
                            int gy = yBase + ly;
                            int gz = zBase + lz;

                            if (IsVoxelPresentUnchecked(gx, gy, gz))
                            {
                                int nx = gx + (axis == 0 ? (positive ? 1 : -1) : 0);
                                int ny = gy + (axis == 1 ? (positive ? 1 : -1) : 0);
                                int nz = gz + (axis == 2 ? (positive ? 1 : -1) : 0);

                                if (!IsVoxelPresent(nx, ny, nz))
                                {
                                    uint faceColor = GetFaceColor(gx, gy, gz, nx, ny, nz);
                                    int idx = u + v * VoxelChunk.Size;
                                    if (!mask[idx])
                                    {
                                        touched[touchedCount++] = idx;
                                    }

                                    mask[idx] = true;
                                    colorMask[idx] = faceColor;
                                    hasAny = true;
                                }
                            }
                        }
                    }

                    if (!hasAny) continue;

                    for (int v = 0; v < vLimit; v++)
                    {
                        for (int u = 0; u < uLimit; u++)
                        {
                            if (mask[u + v * VoxelChunk.Size])
                            {
                                uint currentColor = colorMask[u + v * VoxelChunk.Size];
                                int w, h;
                                for (w = 1; u + w < uLimit; w++)
                                {
                                    int idx = u + w + v * VoxelChunk.Size;
                                    if (!mask[idx] || colorMask[idx] != currentColor)
                                    {
                                        break;
                                    }
                                }
                                bool done = false;
                                for (h = 1; v + h < vLimit; h++)
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

                    for (int ti = 0; ti < touchedCount; ti++)
                    {
                        int idx = touched[ti];
                        mask[idx] = false;
                        colorMask[idx] = 0;
                    }
                }
            }

            var result = new List<(uint color, MeshGeometry3D mesh)>(accumulators.Count);
            foreach (var pair in accumulators)
            {
                AddChunkBoundsSentinel(pair.Value.Positions, pair.Value.Indices, cx, cy, cz);
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

        /// <summary>
        /// WPF/Helix culls GeometryModel3D by bounds. A chunk side may be a perfectly flat mesh
        /// with zero thickness, so at some camera angles it is wrongly treated as outside the frame.
        /// A degenerate triangle spanning the chunk volume gives the model stable 3D bounds and does not render.
        /// </summary>
        private void AddChunkBoundsSentinel(Point3DCollection pos, Int32Collection idx, int cx, int cy, int cz)
        {
            int baseIdx = pos.Count;
            double minX = _min.X + cx * VoxelChunk.Size * _resolution;
            double minY = _min.Y + cy * VoxelChunk.Size * _resolution;
            double minZ = _min.Z + cz * VoxelChunk.Size * _resolution;
            double maxX = _min.X + Math.Min(_sizeX, (cx + 1) * VoxelChunk.Size) * _resolution;
            double maxY = _min.Y + Math.Min(_sizeY, (cy + 1) * VoxelChunk.Size) * _resolution;
            double maxZ = _min.Z + Math.Min(_sizeZ, (cz + 1) * VoxelChunk.Size) * _resolution;

            pos.Add(new Point3D(minX, minY, minZ));
            pos.Add(new Point3D(maxX, maxY, maxZ));
            idx.Add(baseIdx);
            idx.Add(baseIdx + 1);
            idx.Add(baseIdx + 1);
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
            var outwardNormal = positive
                ? (axis switch { 0 => new Vector3D(1, 0, 0), 1 => new Vector3D(0, 1, 0), _ => new Vector3D(0, 0, 1) })
                : (axis switch { 0 => new Vector3D(-1, 0, 0), 1 => new Vector3D(0, -1, 0), _ => new Vector3D(0, 0, -1) });
            AddQuadFanTwoTrianglesForWpf(idx, baseIdx, p1, p2, p3, p4, outwardNormal);
        }

        /// <summary>Веерное разбиение p0–p3; корректирует обход двух треугольников под CCW=WPF‑лицевая в направлении outwardNormal.</summary>
        private static void AddQuadFanTwoTrianglesForWpf(
            Int32Collection idx,
            int baseIdx,
            Point3D p0,
            Point3D p1,
            Point3D p2,
            Point3D p3,
            Vector3D outwardNormal)
        {
            var e10 = new Vector3D(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z);
            var e20 = new Vector3D(p2.X - p0.X, p2.Y - p0.Y, p2.Z - p0.Z);
            var cross = Vector3D.CrossProduct(e10, e20);
            bool flip = Vector3D.DotProduct(cross, outwardNormal) < 0;

            static void Tri(Int32Collection tris, int a, int b, int c, bool swapBc)
            {
                if (!swapBc)
                {
                    tris.Add(a); tris.Add(b); tris.Add(c);
                }
                else
                {
                    tris.Add(a); tris.Add(c); tris.Add(b);
                }
            }

            Tri(idx, baseIdx, baseIdx + 1, baseIdx + 2, flip);
            Tri(idx, baseIdx, baseIdx + 2, baseIdx + 3, flip);
        }

        private bool IsVoxelPresent(int x, int y, int z)
        {
            if (x < 0 || x >= _sizeX || y < 0 || y >= _sizeY || z < 0 || z >= _sizeZ) return false;
            return IsVoxelPresentUnchecked(x, y, z);
        }

        private bool IsVoxelPresentUnchecked(int x, int y, int z)
        {
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

            return _defaultSurfaceColor;
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

        private DiffuseMaterial GetOrCreateMaterial(uint packedColor)
        {
            return _materialCache.GetOrAdd(packedColor, static pc =>
            {
                var brush = new SolidColorBrush(UnpackColor(pc));
                brush.Freeze();
                var material = new DiffuseMaterial(brush);
                material.Freeze();
                return material;
            });
        }
    }
}
