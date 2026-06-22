using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Threading;
using CNCSS.Data;
using CNCSS.Data.Config;
using CNCSS.Logic.Voxel;
using CNCSS.Logic.Voxel.Adaptive;
using CNCSS.UI;
using HelixToolkit.Wpf;

namespace CNCSS.Vis;

/// <summary>Воксельная заготовка на новом адаптивном движке (octree + lazy chunks).</summary>
public sealed class VoxelStockVolume : IStockVolume, IDisposable
{
    private Model3DGroup? _model;
    private GeometryModel3D? _voxelModel;
    private readonly AdaptiveVoxelStock _adaptiveStock;
    private readonly Material _stockMaterial;
    private readonly int _displayLodStride;
    private readonly double _displayGridMm;
    private bool _cutsFrozen;
    private bool _disposed;
    private bool _shellDisplayed;
    private bool _voxelSolidDisplay;
    private bool _pendingMeshRebuild = true;
    private bool _uiApplyQueued;
    private bool _visualApplyScheduled;
    private long _lastVisualApplyTicks;
    private CancellationTokenSource? _meshBuildCts;
    private Task? _meshBuildTask;
    private double _lastCutMs;
    private double _lastRenderMs;
    private double _lastHelixSyncMs;
    private int _chunkCount;
    private int _surfaceVoxelCountEstimate;
    private int _cutRevision;

    private VoxelStockVolume(
        StockVolumeConfig volume,
        StockConstructorConfig? constructor,
        Color stockColor,
        double resolutionMm,
        double displayGridMm)
    {
        Volume = volume;
        Constructor = constructor;
        ResolutionMm = resolutionMm;
        StockColor = stockColor;
        _displayGridMm = VoxelConstants.NormalizeDisplayGridMm(displayGridMm);
        _displayLodStride = VoxelConstants.ComputeDisplayLodStride(resolutionMm, _displayGridMm);
        double coarseVoxel = Math.Max(0.4, resolutionMm * 4.0);
        _adaptiveStock = new AdaptiveVoxelStock(volume, coarseVoxel, resolutionMm);
        _adaptiveStock.InitializeStock();
        _stockMaterial = CreateMaterial(stockColor);
        ActivateShellDisplay();
    }

    public static VoxelStockVolume Create(
        StockVolumeConfig volume,
        StockConstructorConfig? constructor,
        Color? defaultColor = null,
        double resolutionMm = VoxelConstants.VoxelResolutionMm,
        double displayGridMm = VoxelConstants.DisplayGridDefaultMm)
    {
        Color color = defaultColor ?? constructor?.Color.ToWpfColor() ?? Colors.DimGray;
        return new VoxelStockVolume(volume, constructor, color, resolutionMm, displayGridMm);
    }

    public StockVolumeConfig Volume { get; }
    public StockConstructorConfig? Constructor { get; }
    public double ResolutionMm { get; }
    public Color StockColor { get; }
    public AdaptiveVoxelStock AdaptiveStock => _adaptiveStock;
    public int DisplayLodStride => _displayLodStride;
    public double DisplayGridMm => _displayGridMm;

    public Model3D MainModel
    {
        get
        {
            EnsureUiModel();
            return _model!;
        }
    }

    public bool IsDirty => _pendingMeshRebuild;
    public int DirtyChunkCount => _pendingMeshRebuild ? 1 : 0;
    public bool IsCutsFrozen => _cutsFrozen;
    public bool UseVoxelForDisplay => _shellDisplayed;
    public bool HasHelixChunkMeshes => _voxelModel?.Geometry is MeshGeometry3D mesh && mesh.Positions.Count > 0;
    public bool ShellDisplayed => _shellDisplayed;
    public bool ShellWarmupInProgress => _meshBuildTask is { IsCompleted: false };
    public bool IsBackgroundShellWarmupRunning => ShellWarmupInProgress;
    public bool HasPendingVisualWork => _pendingMeshRebuild || ShellWarmupInProgress || _uiApplyQueued;

    public string FormatLodStatusSuffix()
    {
        if (!_shellDisplayed)
        {
            return string.Empty;
        }

        string lod = _displayLodStride > 1 ? $" | lod×{_displayLodStride}" : string.Empty;
        string grid = $" | grid {_displayGridMm:F2} мм ({VoxelConstants.GetDisplayGridLabel(_displayGridMm)})";
        return $" | adaptive chunks {_chunkCount}{grid}{lod}";
    }

    public double LastCutMs => _lastCutMs;
    public double LastHelixSyncMs => _lastHelixSyncMs;
    public double LastRenderMs { get; set; }
    public double RenderLoad => LastRenderMs / Math.Max(1.0, VoxelConstants.PlaybackFrameBudgetMs);
    public int DisplayMeshCount => (_voxelModel?.Geometry != null ? 1 : 0);
    public bool ShowParametricUnderlay => false;
    public int LastRemovedVoxels => _adaptiveStock.LastRemovedVoxels;

    public StockVoxelDisplayStats GetDisplayStats()
    {
        return new StockVoxelDisplayStats(
            Volume.MaxX - Volume.MinX,
            Volume.MaxY - Volume.MinY,
            Volume.MaxZ - Volume.MinZ,
            _surfaceVoxelCountEstimate,
            _chunkCount,
            DirtyChunkCount,
            ResolutionMm,
            _shellDisplayed);
    }

    public void ActivateShellDisplay()
    {
        if (_cutsFrozen || _shellDisplayed)
        {
            return;
        }

        _shellDisplayed = true;
        _pendingMeshRebuild = true;
    }

    public void BeginDisplayWarmup()
    {
        if (_cutsFrozen)
        {
            return;
        }

        _pendingMeshRebuild = true;
        QueueApplyLatestSnapshot();
    }

    public void SetParametricPlaceholder(MeshGeometry3D mesh) => _ = mesh;

    public void CutCylinder(Point3D start, Point3D end, double radius, double fluteLength, Color toolColor)
    {
        ApplyCutMotion(
            CutMotionDescriptor.Linear(start, end),
            new FluteCutProfile { Radius = radius, FluteLength = fluteLength },
            toolColor);
    }

    public void ApplyCutMotion(CutMotionDescriptor motion, FluteCutProfile profile, Color toolColor)
    {
        if (_cutsFrozen)
        {
            return;
        }

        _ = toolColor;
        var sw = Stopwatch.StartNew();
        int removed = 0;

        if (motion.Kind == CutMotionKind.Arc && motion.Arc != null)
        {
            IReadOnlyList<VoxelMotionSegment> segs = ArcCutSegmenter.SegmentArc(
                motion.Arc,
                lineNumber: 0,
                isCuttingMove: true,
                maxChordMm: 0.05);
            foreach (VoxelMotionSegment seg in segs)
            {
                removed += _adaptiveStock.CutCylinder(seg.Start, seg.End, profile.Radius, profile.FluteLength);
            }
        }
        else
        {
            removed = _adaptiveStock.CutCylinder(motion.Start, motion.End, profile.Radius, profile.FluteLength);
        }

        sw.Stop();
        _lastCutMs = sw.Elapsed.TotalMilliseconds;

        bool shellWasDisplayed = _shellDisplayed;
        if (!_shellDisplayed)
        {
            ActivateShellDisplay();
        }

        if (removed > 0 && !_voxelSolidDisplay)
        {
            _voxelSolidDisplay = true;
        }

        if (removed > 0 || !shellWasDisplayed)
        {
            _pendingMeshRebuild = true;
            Interlocked.Increment(ref _cutRevision);
            QueueApplyLatestSnapshot();
        }
    }

    public Task UpdateVisualsAsync()
    {
        if (!_shellDisplayed || !_pendingMeshRebuild || _disposed)
        {
            return Task.CompletedTask;
        }

        if (_meshBuildTask is { IsCompleted: false })
        {
            return _meshBuildTask;
        }

        _meshBuildCts?.Cancel();
        _meshBuildCts?.Dispose();
        _meshBuildCts = new CancellationTokenSource();
        _meshBuildTask = BuildAndApplyMeshAsync(_meshBuildCts.Token);
        return _meshBuildTask;
    }

    public void AttachBoundingSolidPlaceholder(Color diffuse) => _ = diffuse;

    public void FreezeModel() => _cutsFrozen = true;

    private void QueueApplyLatestSnapshot()
    {
        if (!_shellDisplayed || _disposed)
        {
            return;
        }

        Dispatcher? dispatcher = _model?.Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _ = UpdateVisualsAsync();
            return;
        }

        if (_uiApplyQueued)
        {
            return;
        }

        if (ShouldThrottleVisualApply())
        {
            ScheduleDelayedVisualApply(dispatcher);
            return;
        }

        _uiApplyQueued = true;
        dispatcher.BeginInvoke(DispatcherPriority.Render, async () =>
        {
            _uiApplyQueued = false;
            await UpdateVisualsAsync();
            _lastVisualApplyTicks = Stopwatch.GetTimestamp();
        });
    }

    private bool ShouldThrottleVisualApply()
    {
        if (_lastVisualApplyTicks == 0)
        {
            return false;
        }

        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - _lastVisualApplyTicks) * 1000.0 / Stopwatch.Frequency;
        return elapsedMs < GetVisualApplyIntervalMs();
    }

    private void ScheduleDelayedVisualApply(Dispatcher dispatcher)
    {
        if (_visualApplyScheduled)
        {
            return;
        }

        _visualApplyScheduled = true;
        double delayMs = GetVisualApplyIntervalMs();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            _visualApplyScheduled = false;
            await UpdateVisualsAsync();
            _lastVisualApplyTicks = Stopwatch.GetTimestamp();
        };
        timer.Start();
    }

    private async Task BuildAndApplyMeshAsync(CancellationToken ct)
    {
        int buildRevision = Volatile.Read(ref _cutRevision);

        var sw = Stopwatch.StartNew();
        MeshGeometry3D mesh = await _adaptiveStock.BuildMeshAsync(ct, progress: null, displayStride: _displayLodStride);
        sw.Stop();
        _lastRenderMs = sw.Elapsed.TotalMilliseconds;
        LastRenderMs = _lastRenderMs;

        int chunkCount = _adaptiveStock.Octree.EnumerateAllChunks().Count;
        int surfaceEst = Math.Max(0, mesh.Positions.Count);
        bool scheduleNextPass = false;
        var uiApplySw = Stopwatch.StartNew();

        await RunOnUiThreadAsync(() =>
        {
            EnsureUiModel();
            EnsureVoxelModel();
            _voxelModel!.Geometry = mesh;
            _voxelModel.Material = _stockMaterial;
            _voxelModel.BackMaterial = _stockMaterial;
            bool hasNewCuts = Volatile.Read(ref _cutRevision) != buildRevision;
            _pendingMeshRebuild = hasNewCuts;
            scheduleNextPass = hasNewCuts;
            _chunkCount = chunkCount;
            _surfaceVoxelCountEstimate = surfaceEst;
        });

        uiApplySw.Stop();
        _lastHelixSyncMs = uiApplySw.Elapsed.TotalMilliseconds;

        if (scheduleNextPass && !_disposed)
        {
            QueueApplyLatestSnapshot();
        }
    }

    private double GetVisualApplyIntervalMs()
    {
        double baseIntervalMs = 1000.0 / VoxelConstants.VisualUpdateMaxHz;
        double adaptiveIntervalMs = (_lastRenderMs + _lastHelixSyncMs) * 0.80;
        return Math.Max(baseIntervalMs, adaptiveIntervalMs);
    }

    private void EnsureVoxelModel()
    {
        if (_model == null)
        {
            return;
        }

        if (_voxelModel == null)
        {
            _voxelModel = new GeometryModel3D
            {
                Material = _stockMaterial,
                BackMaterial = _stockMaterial
            };
            _model.Children.Add(_voxelModel);
        }
        else if (!_model.Children.Contains(_voxelModel))
        {
            _model.Children.Add(_voxelModel);
        }
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        Dispatcher? dispatcher = _model?.Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private void EnsureUiModel()
    {
        Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(EnsureUiModel);
            return;
        }

        if (_model != null && _model.Dispatcher.CheckAccess())
        {
            return;
        }

        _voxelModel = null;
        _voxelSolidDisplay = false;
        _model = new Model3DGroup();
    }

    private static Material CreateMaterial(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var material = MaterialHelper.CreateMaterial(brush);
        material.Freeze();
        return material;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _meshBuildCts?.Cancel();
        _meshBuildCts?.Dispose();
        _meshBuildCts = null;
        _meshBuildTask = null;
        _voxelModel = null;
        _model = null;
    }
}
