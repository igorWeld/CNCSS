using CNCSS.Logic.ProgramLoading;
using CNCSS.Vis;

namespace CNCSS.Simulation.Execution
{
    /// <summary>Coordinates NC program loading, execution reset and scene preparation.</summary>
    public sealed class ProgramLoadOrchestrator
    {
        private readonly ProgramWorkspace _programWorkspace;
        private readonly ProgramExecutionService _programExecutionService;
        private readonly UI.Hosts.ProgramPlaybackHost _programPlaybackHost;
        private readonly ToolpathSceneBuilder _toolpathSceneBuilder;

        public ProgramLoadOrchestrator(
            ProgramWorkspace programWorkspace,
            ProgramExecutionService programExecutionService,
            UI.Hosts.ProgramPlaybackHost programPlaybackHost,
            ToolpathSceneBuilder toolpathSceneBuilder)
        {
            _programWorkspace = programWorkspace ?? throw new ArgumentNullException(nameof(programWorkspace));
            _programExecutionService = programExecutionService ?? throw new ArgumentNullException(nameof(programExecutionService));
            _programPlaybackHost = programPlaybackHost ?? throw new ArgumentNullException(nameof(programPlaybackHost));
            _toolpathSceneBuilder = toolpathSceneBuilder ?? throw new ArgumentNullException(nameof(toolpathSceneBuilder));
        }

        public ProgramWorkspace Workspace => _programWorkspace;

        public ToolpathSceneBuildResult Load(string filePath)
        {
            ProgramLoadResult loadResult = _programWorkspace.Load(filePath);
            _programExecutionService.LoadProgram(loadResult.Parser.Commands);
            _programExecutionService.SetCurrentIndex(0);
            _programPlaybackHost.ResetToHome();
            return _toolpathSceneBuilder.Build(loadResult);
        }
    }
}
