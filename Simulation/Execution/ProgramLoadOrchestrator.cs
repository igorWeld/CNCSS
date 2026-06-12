using CNCSS.Data;
using CNCSS.Logic.ProgramLoading;
using CNCSS.Machine.Model;
using CNCSS.Vis;

namespace CNCSS.Simulation.Execution
{
    /// <summary>Загрузка УП, reparse с WCS, сброс playback и построение траектории.</summary>
    public sealed class ProgramLoadOrchestrator
    {
        private readonly ProgramWorkspace _programWorkspace;
        private readonly ProgramExecutionService _programExecutionService;
        private readonly UI.Hosts.ProgramPlaybackHost _programPlaybackHost;

        public ProgramLoadOrchestrator(
            ProgramWorkspace programWorkspace,
            ProgramExecutionService programExecutionService,
            UI.Hosts.ProgramPlaybackHost programPlaybackHost)
        {
            _programWorkspace = programWorkspace ?? throw new ArgumentNullException(nameof(programWorkspace));
            _programExecutionService = programExecutionService ?? throw new ArgumentNullException(nameof(programExecutionService));
            _programPlaybackHost = programPlaybackHost ?? throw new ArgumentNullException(nameof(programPlaybackHost));
        }

        public ProgramWorkspace Workspace => _programWorkspace;

        /// <summary>Загрузка файла с reparse по seed-состоянию и построением траектории.</summary>
        public PreparedProgramLoad Load(
            string filePath,
            MachineState seedState,
            MachineDefinition profile,
            double toolStickOutMm)
        {
            PreparedProgramLoad prepared = ProgramLoadPipeline.Prepare(filePath, seedState, profile, toolStickOutMm)
                .ToPreparedLoad();
            ApplyToRuntime(prepared.LoadResult);
            return prepared;
        }

        /// <summary>Пересчёт траектории после смены WCS без перечитывания файла.</summary>
        public PreparedProgramLoad Reparse(
            string[] lines,
            string filePath,
            string fullPath,
            MachineState seedState,
            MachineDefinition profile,
            double toolStickOutMm,
            int preserveUiIndex)
        {
            ProgramLoadResult reprased = ProgramLoadPipeline.ReparseWithLines(lines, filePath, fullPath, seedState);
            var segments = ProgramLoadPipeline.BuildToolpathSegments(reprased.Parser, seedState, profile, toolStickOutMm);
            string stats = ToolpathSceneBuilder.BuildStatsText(segments, fullPath, reprased.Parser);
            var prepared = new PreparedProgramLoad(reprased, segments, stats);
            _programWorkspace.ApplyLoadResult(prepared.LoadResult);
            _programExecutionService.LoadProgram(prepared.LoadResult.Parser.Commands);
            _programExecutionService.SetCurrentIndex(Math.Max(0, preserveUiIndex));
            return prepared;
        }

        private void ApplyToRuntime(ProgramLoadResult loadResult)
        {
            _programWorkspace.ApplyLoadResult(loadResult);
            _programExecutionService.LoadProgram(loadResult.Parser.Commands);
            _programExecutionService.SetCurrentIndex(0);
            _programPlaybackHost.ResetToHome();
        }
    }

    internal static class ProgramLoadPipelineExtensions
    {
        public static PreparedProgramLoad ToPreparedLoad(this ProgramLoadPipeline.PreparedLoad prepared) =>
            new(prepared.LoadResult, prepared.SegmentsWithLines, prepared.StatsText);
    }
}
