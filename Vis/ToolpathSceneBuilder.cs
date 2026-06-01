using System.Text;
using System.IO;
using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Logic.ProgramLoading;
using CNCSS.Machine.Model;

namespace CNCSS.Vis
{
    /// <summary>Builds toolpath scene data and metadata from a loaded NC program.</summary>
    public sealed class ToolpathSceneBuilder
    {
        public ToolpathSceneBuildResult Build(
            ProgramLoadResult loadResult,
            MachineDefinition? machine = null,
            MachineState? initialState = null,
            double toolStickOutMm = 0)
        {
            ArgumentNullException.ThrowIfNull(loadResult);

            var segmentsWithLines = ToolpathBuilder.BuildWithLineNumbers(
                loadResult.Parser,
                initialState,
                machine,
                toolStickOutMm);
            ToolpathSceneBounds? bounds = null;

            foreach (var item in segmentsWithLines)
            {
                if (item.Segment.Kind is not (ToolpathSegmentKind.Linear or ToolpathSegmentKind.Arc))
                {
                    continue;
                }

                foreach (var p in item.Segment.Points)
                {
                    bounds = bounds == null
                        ? new ToolpathSceneBounds(p.X, p.X, p.Y, p.Y, p.Z, p.Z)
                        : bounds.Value.Include(p.X, p.Y, p.Z);
                }
            }

            return new ToolpathSceneBuildResult(
                loadResult,
                segmentsWithLines,
                bounds,
                BuildStatsText(loadResult.FullPath, loadResult.Parser, segmentsWithLines.Select(s => s.Segment).ToList()));
        }

        private static string BuildStatsText(string filePath, GCodeParser parser, List<ToolpathSegment> segments)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Path.GetFileName(filePath));
            sb.AppendLine($"Команд: {parser.Commands.Count}");
            sb.AppendLine($"Сегментов пути: {segments.Count}");
            sb.AppendLine($"Дуг (геометрия): {parser.Commands.Count(c => c.Arc != null)}");
            sb.AppendLine();
            sb.AppendLine("Цвета: Красный G0, Зеленый G1, Голубой G2/G3");
            return sb.ToString();
        }
    }

    public readonly record struct ToolpathSceneBounds(
        double MinX,
        double MaxX,
        double MinY,
        double MaxY,
        double MinZ,
        double MaxZ)
    {
        public ToolpathSceneBounds Include(double x, double y, double z) =>
            new(
                Math.Min(MinX, x),
                Math.Max(MaxX, x),
                Math.Min(MinY, y),
                Math.Max(MaxY, y),
                Math.Min(MinZ, z),
                Math.Max(MaxZ, z));
    }

    public sealed record ToolpathSceneBuildResult(
        ProgramLoadResult LoadResult,
        IReadOnlyList<ToolpathSegmentWithLine> SegmentsWithLines,
        ToolpathSceneBounds? Bounds,
        string StatsText);
}
