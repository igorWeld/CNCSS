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
    }
}
