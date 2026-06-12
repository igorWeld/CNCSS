using CNCSS.Logic.ProgramLoading;
using CNCSS.Vis;

namespace CNCSS.Simulation.Execution
{
    /// <summary>Результат полной подготовки УП: парсинг, reparse с WCS и сегменты траектории.</summary>
    public sealed record PreparedProgramLoad(
        ProgramLoadResult LoadResult,
        IReadOnlyList<ToolpathSegmentWithLine> SegmentsWithLines,
        string StatsText);
}
