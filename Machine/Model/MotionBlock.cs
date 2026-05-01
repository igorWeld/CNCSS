namespace CNCSS.Machine.Model
{
    public enum MotionBlockKind
    {
        Rapid,
        Linear,
        Arc,
        Dwell,
        ToolChange,
        Auxiliary
    }

    public sealed class MotionBlock
    {
        public int LineNumber { get; init; }
        public MotionBlockKind Kind { get; init; }
        public string RawLine { get; init; } = string.Empty;
    }
}
