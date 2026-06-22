using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Machine.Core;
using CNCSS.Machine.Model;
using CNCSS.Simulation.Execution;

namespace CNCSS.UI.Hosts
{
    /// <summary>Coordinates cycle start/pause/tick without direct WPF control access.</summary>
    public sealed class ProgramPlaybackHost
    {
        private readonly CycleCoordinator _cycleCoordinator;
        private readonly PlaybackLoopService _playbackLoopService;
        private readonly ProgramExecutionService _programExecutionService;
        private readonly IMachineCore _machineCore;

        public ProgramPlaybackHost(
            CycleCoordinator cycleCoordinator,
            PlaybackLoopService playbackLoopService,
            ProgramExecutionService programExecutionService,
            IMachineCore machineCore)
        {
            _cycleCoordinator = cycleCoordinator ?? throw new ArgumentNullException(nameof(cycleCoordinator));
            _playbackLoopService = playbackLoopService ?? throw new ArgumentNullException(nameof(playbackLoopService));
            _programExecutionService = programExecutionService ?? throw new ArgumentNullException(nameof(programExecutionService));
            _machineCore = machineCore ?? throw new ArgumentNullException(nameof(machineCore));
            var home = _machineCore.GetHomePhysicalPosition();
            CurrentPosition = new Point3D(home.X, home.Y, home.Z);
            InterpolationProgress = 1.0;
        }

        public Point3D CurrentPosition { get; private set; }
        public double InterpolationProgress { get; private set; }

        public Point3D SegmentStart => _playbackLoopService.SegmentStart;

        public bool IsSegmentInProgress => _playbackLoopService.IsSegmentInProgress;

        public void SetCurrentPosition(Point3D position)
        {
            CurrentPosition = position;
        }

        public void BeginSegmentFromCurrentPosition()
        {
            _playbackLoopService.BeginSegmentFrom(CurrentPosition);
        }

        public bool TryStart(int selectedLineIndex, int lineCount, Func<bool> ensureReady, out int normalizedLineIndex)
        {
            return _cycleCoordinator.TryStart(selectedLineIndex, lineCount, CurrentPosition, ensureReady, out normalizedLineIndex);
        }

        public bool TryPause()
        {
            return _cycleCoordinator.TryFeedHold();
        }

        public ProgramPlaybackTickResult Tick(
            int currentLineIndex,
            Func<MachineState, double> speedResolver,
            double simulationMultiplier,
            double fpsSlowdownFactor)
        {
            var tick = _playbackLoopService.Tick(
                _machineCore.State.IsRunning,
                CurrentPosition,
                currentLineIndex,
                speedResolver,
                simulationMultiplier,
                fpsSlowdownFactor);

            InterpolationProgress = tick.Progress;

            if (tick.Action != PlaybackLoopAction.StopProgram)
            {
                CurrentPosition = tick.CurrentPosition;
                _machineCore.UpdatePosition(CurrentPosition.X, CurrentPosition.Y, CurrentPosition.Z);
                _machineCore.Tick(0.01);
            }

            return new ProgramPlaybackTickResult
            {
                LoopResult = tick,
                CurrentPosition = CurrentPosition,
                ActiveToolNumber = _machineCore.State.ToolNumber,
                IsMachineRunning = _machineCore.State.IsRunning,
                BlockKind = tick.BlockKind
            };
        }

        public ProgramEndState StopForProgramEnd(int selectedLineIndex, int lineCount, int? endProgramMCode, bool rewindToStart, Point3D rewindPosition)
        {
            _programExecutionService.ClearSingleBlockStop();
            _cycleCoordinator.StopForProgramEnd();

            if (rewindToStart && lineCount > 0)
            {
                _programExecutionService.SetCurrentIndex(0);
                CurrentPosition = rewindPosition;
                InterpolationProgress = 1.0;
                _playbackLoopService.BeginSegmentFrom(CurrentPosition);
                return new ProgramEndState(endProgramMCode, rewindToStart, 0, CurrentPosition);
            }

            _programExecutionService.SetCurrentIndex(Math.Max(0, selectedLineIndex));
            return new ProgramEndState(endProgramMCode, rewindToStart, selectedLineIndex, CurrentPosition);
        }

        public void ResetToHome()
        {
            var home = _machineCore.GetHomePhysicalPosition();
            CurrentPosition = new Point3D(home.X, home.Y, home.Z);
            InterpolationProgress = 1.0;
            _cycleCoordinator.Reset(CurrentPosition);
        }
    }

    public sealed class ProgramPlaybackTickResult
    {
        public required PlaybackLoopResult LoopResult { get; init; }
        public required Point3D CurrentPosition { get; init; }
        public required int? ActiveToolNumber { get; init; }
        public required bool IsMachineRunning { get; init; }
        public required MotionBlockKind BlockKind { get; init; }
    }

    public sealed record ProgramEndState(
        int? EndProgramMCode,
        bool RewindToStart,
        int SelectedLineIndex,
        Point3D CurrentPosition);
}
