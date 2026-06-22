using System.Windows.Media.Media3D;
using CNCSS.Controller.Core;

namespace CNCSS.Simulation.Execution
{
    /// <summary>Координирует состояние цикла между контроллером, программой и playback loop.</summary>
    public sealed class CycleCoordinator
    {
        private readonly IControllerCore _controllerCore;
        private readonly ProgramExecutionService _programExecutionService;
        private readonly PlaybackLoopService _playbackLoopService;

        public CycleCoordinator(
            IControllerCore controllerCore,
            ProgramExecutionService programExecutionService,
            PlaybackLoopService playbackLoopService)
        {
            _controllerCore = controllerCore ?? throw new ArgumentNullException(nameof(controllerCore));
            _programExecutionService = programExecutionService ?? throw new ArgumentNullException(nameof(programExecutionService));
            _playbackLoopService = playbackLoopService ?? throw new ArgumentNullException(nameof(playbackLoopService));
        }

        public bool TryStart(int selectedLineIndex, int lineCount, Point3D lastPosition, Func<bool> ensureReady, out int normalizedLineIndex)
        {
            normalizedLineIndex = selectedLineIndex;
            if (lineCount == 0 || !ensureReady())
            {
                return false;
            }

            if (!_controllerCore.CycleStart())
            {
                return false;
            }

            bool resumeCurrentSegment = _playbackLoopService.IsSegmentInProgress;
            if (!resumeCurrentSegment && normalizedLineIndex >= lineCount - 1)
            {
                normalizedLineIndex = 0;
            }

            _programExecutionService.SetCurrentIndex(normalizedLineIndex);
            _programExecutionService.BeginCycle();
            if (!resumeCurrentSegment)
            {
                _playbackLoopService.BeginSegmentFrom(lastPosition);
            }

            return true;
        }

        public bool TryFeedHold()
        {
            if (!_controllerCore.FeedHold())
            {
                return false;
            }

            _programExecutionService.ClearSingleBlockStop();
            return true;
        }

        public void Reset(Point3D homePosition)
        {
            _controllerCore.Reset();
            _programExecutionService.ClearSingleBlockStop();
            _programExecutionService.SetCurrentIndex(0);
            _playbackLoopService.Reset(homePosition);
        }

        public void StopForProgramEnd()
        {
            _programExecutionService.ClearSingleBlockStop();
            _controllerCore.FeedHold();
        }
    }
}
