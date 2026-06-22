using System.Diagnostics;
using System.Windows.Media.Media3D;
using CNCSS.Logic.Voxel.Adaptive;

namespace CNCSS.Simulation.Execution;

/// <summary>
/// Runtime-движок адаптивной воксельной симуляции: загрузка G-кода, start/pause/reset/cancel, mesh updates.
/// </summary>
public sealed class VoxelSimulationEngine
{
    private readonly AdaptiveGCodeInterpreter _interpreter;
    private readonly StockSnapshotStore _snapshotStore;
    private readonly object _gate = new();

    private IReadOnlyList<VoxelMotionSegment> _segments = Array.Empty<VoxelMotionSegment>();
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private bool _paused;
    private int _segmentIndex;
    private Stopwatch _elapsed = new();
    private string? _loadedProgramPath;

    public VoxelSimulationEngine(
        AdaptiveVoxelStock stock,
        VoxelTool tool,
        AdaptiveGCodeInterpreter interpreter,
        StockSnapshotStore? snapshotStore = null)
    {
        Stock = stock;
        Tool = tool;
        _interpreter = interpreter;
        _snapshotStore = snapshotStore ?? new StockSnapshotStore();
    }

    public AdaptiveVoxelStock Stock { get; private set; }

    public VoxelTool Tool { get; }

    public bool IsRunning => _runTask is { IsCompleted: false } && !_paused;

    public bool IsPaused => _paused;

    public int SegmentIndex => _segmentIndex;

    public int SegmentCount => _segments.Count;

    public event Action<double>? ProgressChanged;

    public event Action<string>? StatusChanged;

    public event Action<MeshGeometry3D>? MeshReady;

    public event Action<double>? FpsChanged;

    public event Action<TimeSpan>? TimeChanged;

    public void LoadProgram(string filePath, bool includeRapidMoves = false, double arcChordMm = 0.05)
    {
        _loadedProgramPath = filePath;
        _interpreter.Load(filePath);
        _segments = _interpreter.BuildMotionSegments(includeRapidMoves, arcChordMm);
        _segmentIndex = 0;
        StatusChanged?.Invoke($"Program loaded: {_segments.Count} segments");
    }

    public async Task StartAsync(CancellationToken externalToken = default)
    {
        lock (_gate)
        {
            if (_segments.Count == 0)
            {
                StatusChanged?.Invoke("No motion segments loaded.");
                return;
            }

            if (_runTask is { IsCompleted: false })
            {
                _paused = false;
                StatusChanged?.Invoke("Resumed");
                return;
            }

            _paused = false;
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _elapsed.Start();
            _runTask = RunLoopAsync(_runCts.Token);
        }

        await _runTask;
    }

    public void Pause()
    {
        _paused = true;
        StatusChanged?.Invoke("Paused");
    }

    public void Resume()
    {
        _paused = false;
        StatusChanged?.Invoke("Resumed");
    }

    public void Cancel()
    {
        _runCts?.Cancel();
        StatusChanged?.Invoke("Cancelling...");
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        Cancel();
        if (_runTask != null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        _elapsed.Reset();
        _segmentIndex = 0;
        Stock.InitializeStock();
        MeshGeometry3D mesh = await Stock.BuildMeshAsync(ct);
        MeshReady?.Invoke(mesh);
        ProgressChanged?.Invoke(0);
        StatusChanged?.Invoke("Reset completed");
    }

    public async Task SaveSnapshotAsync(string filePath, CancellationToken ct = default)
    {
        var snapshot = new VoxelSimulationSnapshot(
            _loadedProgramPath,
            _segmentIndex,
            _segments.Count,
            Stock.ExportState());
        await _snapshotStore.SaveAsync(filePath, snapshot.StockState, ct);
        StatusChanged?.Invoke("Stock snapshot saved");
    }

    public async Task LoadSnapshotAsync(string filePath, CancellationToken ct = default)
    {
        AdaptiveVoxelStockState state = await _snapshotStore.LoadAsync(filePath, ct);
        Stock = AdaptiveVoxelStock.ImportState(state);
        MeshGeometry3D mesh = await Stock.BuildMeshAsync(ct);
        MeshReady?.Invoke(mesh);
        StatusChanged?.Invoke("Stock snapshot loaded");
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        int frameCounter = 0;
        Stopwatch fpsWindow = Stopwatch.StartNew();
        StatusChanged?.Invoke("Running");

        while (_segmentIndex < _segments.Count)
        {
            ct.ThrowIfCancellationRequested();
            if (_paused)
            {
                await Task.Delay(25, ct);
                continue;
            }

            VoxelMotionSegment seg = _segments[_segmentIndex];
            if (seg.IsCuttingMove)
            {
                Stock.CutCylinder(seg.Start, seg.End, Tool.CutRadius, Tool.CutLength);
            }

            _segmentIndex++;
            frameCounter++;
            TimeChanged?.Invoke(_elapsed.Elapsed);
            ProgressChanged?.Invoke((double)_segmentIndex / Math.Max(1, _segments.Count));

            if (_segmentIndex % 25 == 0 || _segmentIndex == _segments.Count)
            {
                MeshGeometry3D mesh = await Stock.BuildMeshAsync(ct);
                MeshReady?.Invoke(mesh);
            }

            if (fpsWindow.ElapsedMilliseconds >= 1000)
            {
                double fps = frameCounter / Math.Max(1e-6, fpsWindow.Elapsed.TotalSeconds);
                FpsChanged?.Invoke(fps);
                frameCounter = 0;
                fpsWindow.Restart();
            }
        }

        _elapsed.Stop();
        MeshGeometry3D finalMesh = await Stock.BuildMeshAsync(ct);
        MeshReady?.Invoke(finalMesh);
        StatusChanged?.Invoke("Completed");
    }

    private readonly record struct VoxelSimulationSnapshot(
        string? ProgramPath,
        int SegmentIndex,
        int SegmentCount,
        AdaptiveVoxelStockState StockState);
}
