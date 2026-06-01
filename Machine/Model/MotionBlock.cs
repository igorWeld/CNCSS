namespace CNCSS.Machine.Model
{
    /// <summary>Вид кадра траектории для потока исполнения программы.</summary>
    public enum MotionBlockKind
    {
        Rapid,
        Linear,
        Arc,
        Dwell,
        ToolChange,
        Auxiliary
    }

    /// <summary>Логический блок движения/вспомогательной команды, извлечённый из строки G-кода.</summary>
    public sealed class MotionBlock
    {
        public int LineNumber { get; init; }
        public MotionBlockKind Kind { get; init; }
        public string RawLine { get; init; } = string.Empty;
        public bool HasTarget { get; init; }
        public double TargetX { get; init; }
        public double TargetY { get; init; }
        public double TargetZ { get; init; }
        public bool IsProgramStop { get; init; }
        public bool IsOptionalStop { get; init; }
        public bool IsProgramEnd { get; init; }
        public int? EndProgramMCode { get; init; }
    }
}
