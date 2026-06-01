using System.Diagnostics;

namespace CNCSS.Vis
{
    /// <summary>Lightweight rolling telemetry for voxel operations that are sensitive to UI frame time.</summary>
    public sealed class VoxelPerformanceMonitor
    {
        private long _sampleCount;
        private double _averageMeshRefreshMs;
        private double _maxMeshRefreshMs;

        public long SampleCount => _sampleCount;
        public double AverageMeshRefreshMs => _averageMeshRefreshMs;
        public double MaxMeshRefreshMs => _maxMeshRefreshMs;

        public IDisposable MeasureMeshRefresh(string profileName, int dirtyChunkCount)
        {
            return new Scope(this, profileName, dirtyChunkCount);
        }

        private void RecordMeshRefresh(string profileName, int dirtyChunkCount, double elapsedMs)
        {
            _sampleCount++;
            _averageMeshRefreshMs += (elapsedMs - _averageMeshRefreshMs) / _sampleCount;
            _maxMeshRefreshMs = Math.Max(_maxMeshRefreshMs, elapsedMs);

            Debug.WriteLine(
                $"[VoxelPerf] mesh profile={profileName} dirty={dirtyChunkCount} elapsed={elapsedMs:F1}ms avg={_averageMeshRefreshMs:F1}ms max={_maxMeshRefreshMs:F1}ms");
        }

        private sealed class Scope : IDisposable
        {
            private readonly VoxelPerformanceMonitor _owner;
            private readonly string _profileName;
            private readonly int _dirtyChunkCount;
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private bool _disposed;

            public Scope(VoxelPerformanceMonitor owner, string profileName, int dirtyChunkCount)
            {
                _owner = owner;
                _profileName = profileName;
                _dirtyChunkCount = dirtyChunkCount;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _stopwatch.Stop();
                _owner.RecordMeshRefresh(_profileName, _dirtyChunkCount, _stopwatch.Elapsed.TotalMilliseconds);
                _disposed = true;
            }
        }
    }
}
