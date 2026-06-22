namespace CNCSS.Logic.ProgramLoading
{
    /// <summary>Результат загрузки NC-программы: исходные строки, разобранный parser и найденные инструменты.</summary>
    public sealed class ProgramLoadResult
    {
        public required string FilePath { get; init; }
        public required string FullPath { get; init; }
        public required string[] Lines { get; init; }
        public required GCodeParser Parser { get; init; }
        public required IReadOnlyList<int> ToolNumbers { get; init; }
    }
}
