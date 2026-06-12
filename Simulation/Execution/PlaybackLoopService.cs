using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Simulation.Execution
{
    /// <summary>Действие после тика воспроизведения (продолжить, остановить программу, пауза по опции и т.д.).</summary>
    public enum PlaybackLoopAction
    {
        None,
        StopProgram,
        PauseForOptionalStop,
        PauseForSingleBlock
    }

    /// <summary>Результат одного вызова такта цикла воспроизведения: индекс строки, интервал таймера, прогресс интерполяции.</summary>
    public sealed class PlaybackLoopResult
    {
        public PlaybackLoopAction Action { get; init; } = PlaybackLoopAction.None;
        public int? EndProgramMCode { get; init; }
        public bool RewindToStart { get; init; }
        public bool HasIndexUpdate { get; init; }
        public int NewIndex { get; init; }
        public int TimerIntervalMs { get; init; } = 10;
        public double Progress { get; init; } = 1.0;
        public Point3D CurrentPosition { get; init; }
    }

    /// <summary>Связывает интерполяцию движения с <see cref="ProgramExecutionService"/> и публикует переходы между кадрами УП.</summary>
    public sealed class PlaybackLoopService
    {
        private readonly ProgramExecutionService _programExecutionService;
        private Point3D _start;
        private Point3D _target;
        private double _progress = 1.0;
        private ArcGeometry? _currentArc;

        public bool IsSegmentInProgress => _progress < 1.0;

        public Point3D SegmentStart => _start;

        public double SegmentProgress => _progress;

        public PlaybackLoopService(ProgramExecutionService programExecutionService)
        {
            _programExecutionService = programExecutionService;
        }

        public void Reset(Point3D homePosition)
        {
            _start = homePosition;
            _target = homePosition;
            _progress = 1.0;
            _currentArc = null;
        }

        public void BeginSegmentFrom(Point3D from)
        {
            _start = from;
            _target = from;
            _progress = 1.0;
            _currentArc = null;
        }

        public PlaybackLoopResult Tick(
            bool machineIsRunning,
            Point3D lastPosition,
            int currentLineIndex,
            Func<MachineState, double> speedResolver,
            double simulationMultiplier,
            double fpsSlowdownFactor)
        {
            if (!machineIsRunning)
            {
                return new PlaybackLoopResult { CurrentPosition = lastPosition, Progress = _progress };
            }

            int intervalMs = 10;
            bool hasIndexUpdate = false;
            int newIndex = -1;

            if (_progress >= 1.0)
            {
                var decision = _programExecutionService.GetNextMoveDecision(lastPosition.X, lastPosition.Y, lastPosition.Z);

                if (decision.EndOfProgram)
                {
                    return new PlaybackLoopResult
                    {
                        Action = PlaybackLoopAction.StopProgram,
                        EndProgramMCode = decision.EndProgramMCode,
                        RewindToStart = decision.RewindToStart,
                        CurrentPosition = lastPosition,
                        Progress = _progress
                    };
                }

                if (decision.StopForOptional)
                {
                    return new PlaybackLoopResult
                    {
                        Action = PlaybackLoopAction.PauseForOptionalStop,
                        HasIndexUpdate = currentLineIndex != decision.NextIndex,
                        NewIndex = decision.NextIndex,
                        CurrentPosition = lastPosition,
                        Progress = _progress
                    };
                }

                if (decision.HasMove)
                {
                    _start = lastPosition;
                    _target = new Point3D(decision.TargetX, decision.TargetY, decision.TargetZ);
                    var init = InterpolationService.InitializeSegment(_start, _target);
                    _progress = init.progress;
                    intervalMs = init.intervalMs;
                    hasIndexUpdate = currentLineIndex != decision.NextIndex;
                    newIndex = decision.NextIndex;
                    _currentArc = _programExecutionService.GetCurrentCommand()?.Arc;
                }
            }

            if (_progress < 1.0)
            {
                var cmd = _programExecutionService.GetCurrentCommand();
                double speedMmPerSec = cmd != null ? speedResolver(cmd.EndState) : 10.0;
                _progress = InterpolationService.AdvanceProgress(
                    _progress,
                    _start,
                    _target,
                    speedMmPerSec,
                    simulationMultiplier,
                    fpsSlowdownFactor);
            }

            Point3D currentPos = InterpolationService.ComputePosition(_start, _target, _progress, _currentArc);

            if (_programExecutionService.ShouldHoldForSingleBlock(_progress))
            {
                return new PlaybackLoopResult
                {
                    Action = PlaybackLoopAction.PauseForSingleBlock,
                    HasIndexUpdate = hasIndexUpdate,
                    NewIndex = newIndex,
                    TimerIntervalMs = intervalMs,
                    CurrentPosition = currentPos,
                    Progress = _progress
                };
            }

            return new PlaybackLoopResult
            {
                HasIndexUpdate = hasIndexUpdate,
                NewIndex = newIndex,
                TimerIntervalMs = intervalMs,
                CurrentPosition = currentPos,
                Progress = _progress
            };
        }
    }
}
