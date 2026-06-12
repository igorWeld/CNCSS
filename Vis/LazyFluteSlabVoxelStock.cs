using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Vis
{
    /// <summary>
    /// Адаптивная воксельная верификация: визуализируется только локальный slab у инструмента (XY от прохода, Z по flute);
    /// сплошой бокс заготовки не рисуется.
    /// </summary>
    public sealed class LazyFluteSlabVoxelStock : IStockVolume
    {
        private const int MaxHorizCells = 320;
        private const int MaxNz = 384;

        /// <summary>Запас по XY в каждую сторону в единицах вокселя (только вокруг геометрии съёма, не «широкое окно»).</summary>
        private const double NearToolPadVoxels = 2.5;

        /// <summary>Минимальный размер slab по осям в ячейках (узкий коридор).</summary>
        private const int MinHorizCells = 6;

        /// <summary>
        /// Не выделять/пересчитывать воксели, пока траектория кончика не приблизилась к заготовке по Z
        /// (типичный быстрый вылет над заготовкой).
        /// </summary>
        private const double VoxelEngagementMaxGapMm = 1.0;

        /// <summary>Цвет меша при разрешении не 0.01 мм (если класс когда-либо используют с другим шагом).</summary>
        private static readonly Color LoadedVoxelSurfaceColorFallbackWpf = Color.FromRgb(0x2E, 0x8B, 0x57);

        private readonly ReaderWriterLockSlim _gate = new(LockRecursionPolicy.NoRecursion);
        private readonly Model3DGroup _mainModelContainer = new();
        private readonly Model3DGroup _presentationRoot = new();
        private GeometryModel3D? _voxelModel;

        private readonly double _stockMinX;
        private readonly double _stockMaxX;
        private readonly double _stockMinY;
        private readonly double _stockMaxY;
        private readonly double _stockMinZ;
        private readonly double _stockMaxZ;
        private readonly double _resolution;
        private byte[] _material = Array.Empty<byte>();
        private int _nx;
        private int _ny;
        private int _nz;
        private double _originX;
        private double _originY;
        private double _zSlabMin;

        private volatile bool _isFrozen;
        private volatile bool _dirtyMesh;

        private readonly record struct StoredCut(Point3D Start, Point3D End, double Radius, double FluteLength);

        private readonly List<StoredCut> _cuts = new();

        public Model3D MainModel => _mainModelContainer;

        /// <inheritdoc />
        public bool IsDirty { get; private set; } = true;

        public int DirtyChunkCount => IsDirty ? 1 : 0;

        /// <inheritdoc />
        public bool IsCutsFrozen => _isFrozen;

        /// <summary>Число ячеек активного slab (не весь заготовочный параллелепипед).</summary>
        public long AllocatedVoxelCount => _material.LongLength;

        public double Resolution => _resolution;

        /// <summary>
        /// Есть ли у отрезка шанс снять материал у заготовки по Z в пределах <see cref="VoxelEngagementMaxGapMm"/> мм
        /// от граней (для координации с GPU-сессией без дублирования логики).
        /// </summary>
        public bool IsCutSegmentWithinVoxelEngagementRange(Point3D start, Point3D end, double fluteLength) =>
            SegmentCanEngageStockForVoxels(start, end, fluteLength);

        public LazyFluteSlabVoxelStock(double width, double depth, double height, double resolution, Point3D center, double maxZ)
        {
            if (resolution <= 0 || width <= 0 || depth <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resolution));
            }

            _resolution = resolution;
            _stockMinX = center.X - width * 0.5;
            _stockMaxX = center.X + width * 0.5;
            _stockMinY = center.Y - depth * 0.5;
            _stockMaxY = center.Y + depth * 0.5;
            _stockMaxZ = maxZ;
            _stockMinZ = maxZ - height;

            _mainModelContainer.Children.Add(_presentationRoot);
        }

        /// <inheritdoc />
        public void AttachBoundingSolidPlaceholder(Color diffuse) { }

        /// <inheritdoc />
        public void FreezeModel()
        {
            _isFrozen = true;
            IsDirty = false;
        }

        /// <inheritdoc />
        public void CutCylinder(Point3D start, Point3D end, double radius, double fluteLength, Color toolColor)
        {
            if (_isFrozen)
            {
                return;
            }

            if (!SegmentCanEngageStockForVoxels(start, end, fluteLength))
            {
                return;
            }

            _gate.EnterWriteLock();
            try
            {
                var cut = new StoredCut(start, end, radius, fluteLength);
                _cuts.Add(cut);

                bool recreated = EnsureSlabAroundTool(start, end, radius, fluteLength);
                if (recreated)
                {
                    ApplyAllCutsLocked();
                }
                else
                {
                    SubtractOneCylinderLocked(cut);
                }

                IsDirty = true;
                _dirtyMesh = true;
            }
            finally
            {
                _gate.ExitWriteLock();
            }
        }

        /// <summary>
        /// Сегмент может снять материал у заготовки в пределах <see cref="VoxelEngagementMaxGapMm"/> мм по Z
        /// от её граней — иначе (вылет выше верха, только воздух) воксели не трогаем.
        /// </summary>
        private bool SegmentCanEngageStockForVoxels(Point3D start, Point3D end, double fluteLength)
        {
            double g = VoxelEngagementMaxGapMm;
            double tipLowZ = Math.Min(start.Z, end.Z);
            if (tipLowZ > _stockMaxZ + g)
            {
                return false;
            }

            double cutTopZ = Math.Max(start.Z, end.Z) + fluteLength;
            if (cutTopZ < _stockMinZ - g)
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public async Task UpdateVisualsAsync()
        {
            if (!_dirtyMesh)
            {
                IsDirty = false;
                return;
            }

            byte[] snapshot;
            int nx;
            int ny;
            int nz;
            double ox;
            double oy;
            double z0;
            double res;

            _gate.EnterReadLock();
            try
            {
                if (_material.Length == 0 || _nx == 0 || _ny == 0 || _nz == 0)
                {
                    IsDirty = false;
                    _dirtyMesh = false;
                    return;
                }

                snapshot = GC.AllocateUninitializedArray<byte>(_material.Length);
                _material.AsSpan().CopyTo(snapshot);
                nx = _nx;
                ny = _ny;
                nz = _nz;
                ox = _originX;
                oy = _originY;
                z0 = _zSlabMin;
                res = _resolution;
            }
            finally
            {
                _gate.ExitReadLock();
            }

            MeshGeometry3D built = await Task.Run(() =>
                VoxelSurfaceMesher.BuildNaiveOuterSurface(snapshot.AsSpan(), nx, ny, nz, ox, oy, z0, res)).ConfigureAwait(true);

            built.Freeze();

            Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (dispatcher.CheckAccess())
            {
                ApplyBuiltMesh(built);
            }
            else
            {
                await dispatcher.InvokeAsync(() => ApplyBuiltMesh(built));
            }

            _dirtyMesh = false;
            IsDirty = false;
        }

        private void ApplyBuiltMesh(MeshGeometry3D built)
        {
            if (built.Positions == null || built.Positions.Count == 0)
            {
                if (_voxelModel != null)
                {
                    _presentationRoot.Children.Remove(_voxelModel);
                    _voxelModel = null;
                }

                return;
            }

            var brush = new SolidColorBrush(GetLoadedVoxelSurfaceColor());
            brush.Freeze();
            var mat = new DiffuseMaterial(brush);
            mat.Freeze();

            if (_voxelModel == null)
            {
                _voxelModel = new GeometryModel3D(built, mat) { BackMaterial = mat };
                _presentationRoot.Children.Add(_voxelModel);
            }
            else
            {
                _voxelModel.Geometry = built;
                _voxelModel.Material = mat;
                _voxelModel.BackMaterial = mat;
            }
        }

        /// <summary>Сетка 0.01 мм — бирюзовый (cyan); иначе резервный оттенок.</summary>
        private Color GetLoadedVoxelSurfaceColor()
        {
            return Math.Abs(_resolution - ProjectConstants.RES_HIGH) <= 1e-9
                ? Color.FromRgb(0x00, 0xCE, 0xD1)
                : LoadedVoxelSurfaceColorFallbackWpf;
        }

        /// <remarks>Вызывается только под <see cref="_gate"/> write lock.</remarks>
        private bool EnsureSlabAroundTool(Point3D start, Point3D end, double radius, double fluteLength)
        {
            int wantNz = Math.Max(4, (int)Math.Ceiling(fluteLength / _resolution));
            wantNz = Math.Min(MaxNz, wantNz);

            double pad = NearToolPadVoxels * _resolution;
            double minPx = Math.Min(start.X, end.X) - radius - pad;
            double maxPx = Math.Max(start.X, end.X) + radius + pad;
            double minPy = Math.Min(start.Y, end.Y) - radius - pad;
            double maxPy = Math.Max(start.Y, end.Y) + radius + pad;

            minPx = Math.Clamp(minPx, _stockMinX, _stockMaxX);
            maxPx = Math.Clamp(maxPx, _stockMinX, _stockMaxX);
            minPy = Math.Clamp(minPy, _stockMinY, _stockMaxY);
            maxPy = Math.Clamp(maxPy, _stockMinY, _stockMaxY);

            double needW = Math.Max(_resolution * MinHorizCells, maxPx - minPx);
            double needD = Math.Max(_resolution * MinHorizCells, maxPy - minPy);

            int wantNx = Math.Min(MaxHorizCells, (int)Math.Ceiling(needW / _resolution));
            int wantNy = Math.Min(MaxHorizCells, (int)Math.Ceiling(needD / _resolution));
            wantNx = Math.Clamp(wantNx, MinHorizCells, MaxHorizCells);
            wantNy = Math.Clamp(wantNy, MinHorizCells, MaxHorizCells);

            double slabW = wantNx * _resolution;
            double slabD = wantNy * _resolution;

            double snappedOx = SnapSlabOrigin(minPx, maxPx, slabW, _stockMinX, _stockMaxX);
            double snappedOy = SnapSlabOrigin(minPy, maxPy, slabD, _stockMinY, _stockMaxY);

            double tipRefZ = Math.Min(start.Z, end.Z);
            double zBottom = SnapDownClamp(tipRefZ, _stockMinZ, Math.Max(_stockMinZ, _stockMaxZ - wantNz * _resolution));

            double cx = (start.X + end.X) * 0.5;
            double cy = (start.Y + end.Y) * 0.5;

            bool moved = _material.Length == 0 ||
                         wantNx != _nx ||
                         wantNy != _ny ||
                         wantNz != _nz ||
                         Math.Abs(snappedOx - _originX) > _resolution * 0.001 ||
                         Math.Abs(snappedOy - _originY) > _resolution * 0.001 ||
                         Math.Abs(zBottom - _zSlabMin) > _resolution * 0.001;

            if (!moved && IsInsideWarmZoneNearTool(cx, cy, tipRefZ, fluteLength))
            {
                return false;
            }

            _nx = wantNx;
            _ny = wantNy;
            _nz = wantNz;
            _originX = snappedOx;
            _originY = snappedOy;
            _zSlabMin = zBottom;

            int cells = checked(_nx * _ny * _nz);
            if (_material.Length != cells)
            {
                _material = new byte[cells];
            }

            FillSolidInsideStockLocked();
            return true;
        }

        /// <summary>Смещение нижнего угла slab по одной оси так, чтобы покрыть [minP, maxP] при ширине slabSpan.</summary>
        private double SnapSlabOrigin(double minP, double maxP, double slabSpan, double stockLo, double stockHi)
        {
            double hi = Math.Max(stockLo, stockHi - slabSpan);
            double ox = SnapDownClamp(minP, stockLo, hi);
            if (ox + slabSpan < maxP - 1e-12)
            {
                ox = SnapDownClamp(maxP - slabSpan, stockLo, hi);
            }

            return ox;
        }

        /// <summary>Небольшой «коридор» у инструмента: у края slab уже подгружаем заново, без 10% полей по всей ширине.</summary>
        private bool IsInsideWarmZoneNearTool(double px, double py, double tipZ, double fluteLength)
        {
            if (_material.Length == 0 || _nx == 0 || _ny == 0 || _nz == 0)
            {
                return false;
            }

            double marginXY = _resolution * 2.0;
            double marginZ = Math.Max(_resolution * 6, fluteLength * 0.15);
            double x0 = _originX + marginXY;
            double x1 = _originX + _nx * _resolution - marginXY;
            double y0 = _originY + marginXY;
            double y1 = _originY + _ny * _resolution - marginXY;
            double z0 = _zSlabMin - marginZ;
            double z1 = _zSlabMin + _nz * _resolution + marginZ;

            return px >= x0 && px <= x1 && py >= y0 && py <= y1 && tipZ >= z0 && tipZ <= z1;
        }

        private double SnapDownClamp(double value, double minV, double maxV)
        {
            double s = Math.Floor(value / _resolution + 1e-9) * _resolution;
            return Math.Clamp(s, minV, Math.Max(minV + _resolution * 4, maxV));
        }

        private void FillSolidInsideStockLocked()
        {
            for (int z = 0; z < _nz; z++)
            {
                double wz0 = _zSlabMin + z * _resolution;
                double wz1 = wz0 + _resolution;
                int zStride = z * (_nx * _ny);

                for (int y = 0; y < _ny; y++)
                {
                    double wy0 = _originY + y * _resolution;
                    double wy1 = wy0 + _resolution;

                    int yStride = zStride + _nx * y;
                    for (int x = 0; x < _nx; x++)
                    {
                        double wx0 = _originX + x * _resolution;
                        double wx1 = wx0 + _resolution;

                        bool inStock =
                            wx1 > _stockMinX && wx0 < _stockMaxX &&
                            wy1 > _stockMinY && wy0 < _stockMaxY &&
                            wz1 > _stockMinZ && wz0 < _stockMaxZ;

                        _material[x + yStride] = (byte)(inStock ? 1 : 0);
                    }
                }
            }
        }

        private void ApplyAllCutsLocked()
        {
            for (int i = 0; i < _cuts.Count; i++)
            {
                StoredCut c = _cuts[i];
                if (CutOverlapsCurrentSlab(c))
                {
                    SubtractOneCylinderLocked(c);
                }
            }
        }

        private bool CutOverlapsCurrentSlab(StoredCut c)
        {
            double minZseg = Math.Min(c.Start.Z, c.End.Z);
            double maxZseg = Math.Max(c.Start.Z, c.End.Z) + c.FluteLength;
            double slabTop = _zSlabMin + _nz * _resolution;
            if (maxZseg < _zSlabMin - _resolution || minZseg > slabTop + _resolution)
            {
                return false;
            }

            double minXw = Math.Min(c.Start.X, c.End.X) - c.Radius;
            double maxXw = Math.Max(c.Start.X, c.End.X) + c.Radius;
            double minYw = Math.Min(c.Start.Y, c.End.Y) - c.Radius;
            double maxYw = Math.Max(c.Start.Y, c.End.Y) + c.Radius;

            double sx1 = _originX + _nx * _resolution;
            double sy1 = _originY + _ny * _resolution;
            if (maxXw < _originX - _resolution || minXw > sx1 + _resolution ||
                maxYw < _originY - _resolution || minYw > sy1 + _resolution)
            {
                return false;
            }

            return true;
        }

        /// <summary>XY-профиль + полоса по Z, как в <see cref="VoxelStock"/>.</summary>
        private void SubtractOneCylinderLocked(StoredCut cut)
        {
            Point3D start = cut.Start;
            Point3D end = cut.End;
            double radius = cut.Radius;
            double fluteLength = cut.FluteLength;

            double r2 = radius * radius;
            Vector3D dir = end - start;
            double len2_xy = dir.X * dir.X + dir.Y * dir.Y;

            int minX = Math.Max(0, (int)Math.Floor((Math.Min(start.X, end.X) - radius - _originX) / _resolution));
            int maxX = Math.Min(_nx - 1, (int)Math.Floor((Math.Max(start.X, end.X) + radius - _originX) / _resolution));
            int minY = Math.Max(0, (int)Math.Floor((Math.Min(start.Y, end.Y) - radius - _originY) / _resolution));
            int maxY = Math.Min(_ny - 1, (int)Math.Floor((Math.Max(start.Y, end.Y) + radius - _originY) / _resolution));

            if (minX > maxX || minY > maxY || _nz <= 0)
            {
                return;
            }

            for (int iy = minY; iy <= maxY; iy++)
            {
                double py = _originY + iy * _resolution + _resolution * 0.5;
                int yStride = _nx * iy;

                for (int ix = minX; ix <= maxX; ix++)
                {
                    double px = _originX + ix * _resolution + _resolution * 0.5;

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

                    int gzStart = Math.Max(0, (int)Math.Floor((currentTipZ - _zSlabMin) / _resolution));
                    int gzEnd = Math.Min(_nz - 1, (int)Math.Floor((currentTopZ - _zSlabMin) / _resolution));

                    for (int iz = gzStart; iz <= gzEnd; iz++)
                    {
                        int idx = ix + yStride + _nx * _ny * iz;
                        _material[idx] = 0;
                    }
                }
            }
        }

    }
}
