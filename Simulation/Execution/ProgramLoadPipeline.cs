using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Logic.ProgramLoading;
using CNCSS.Machine.Model;
using CNCSS.Vis;

namespace CNCSS.Simulation.Execution
{
    /// <summary>CPU-тяжёлая подготовка УП (парсинг, reparse, траектория) вне UI-потока.</summary>
    public static class ProgramLoadPipeline
    {
        public sealed record PreparedLoad(
            ProgramLoadResult LoadResult,
            IReadOnlyList<ToolpathSegmentWithLine> SegmentsWithLines,
            ToolpathSceneBounds? Bounds,
            string StatsText);

        public static PreparedLoad Prepare(
            string filePath,
            MachineState seedState,
            MachineDefinition profile,
            double toolStickOutMm)
        {
            var loader = new ProgramLoader();
            ProgramLoadResult initial = loader.Load(filePath);
            ProgramLoadResult reprased = ReparseWithLines(initial.Lines, initial.FilePath, initial.FullPath, seedState);

            var sceneBuilder = new ToolpathSceneBuilder();
            ToolpathSceneBuildResult scene = sceneBuilder.Build(
                reprased,
                profile,
                seedState.Clone(),
                toolStickOutMm);

            return new PreparedLoad(reprased, scene.SegmentsWithLines, scene.Bounds, scene.StatsText);
        }

        public static IReadOnlyList<ToolpathSegmentWithLine> BuildToolpathSegments(
            GCodeParser parser,
            MachineState seedState,
            MachineDefinition profile,
            double toolStickOutMm) =>
            ToolpathBuilder.BuildWithLineNumbers(parser, seedState.Clone(), profile, toolStickOutMm);

        public static ProgramLoadResult ReparseWithLines(
            string[] lines,
            string filePath,
            string fullPath,
            MachineState seedState)
        {
            var parser = new GCodeParser(seedState.Clone());
            for (int i = 0; i < lines.Length; i++)
            {
                parser.ProcessLine(lines[i], i + 1);
            }

            int[] toolNumbers = parser.Commands
                .Where(c => c.ToolNumber.HasValue)
                .Select(c => c.ToolNumber!.Value)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();

            return new ProgramLoadResult
            {
                FilePath = filePath,
                FullPath = fullPath,
                Lines = lines,
                Parser = parser,
                ToolNumbers = toolNumbers
            };
        }
    }
}
