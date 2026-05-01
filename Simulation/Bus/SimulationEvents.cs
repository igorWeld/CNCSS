namespace CNCSS.Simulation.Bus
{
    public sealed record ControllerCommandEvent(
        string Command,
        string? Payload,
        DateTime TimestampUtc) : SimulationEvent(TimestampUtc);

    public sealed record MachineStateChangedEvent(
        double X,
        double Y,
        double Z,
        bool IsRunning,
        int? ToolNumber,
        double FeedRate,
        double SpindleSpeed,
        bool IsSpindleOn,
        bool IsSpindleCW,
        bool IsCoolantOn,
        string CoordinateSystem,
        double OffsetX,
        double OffsetY,
        double OffsetZ,
        DateTime TimestampUtc) : SimulationEvent(TimestampUtc);

    public sealed record AlarmRaisedEvent(
        string Code,
        string Message,
        DateTime TimestampUtc) : SimulationEvent(TimestampUtc);

    public sealed record MdiModeChangedEvent(
        string Mode,
        DateTime TimestampUtc) : SimulationEvent(TimestampUtc);

    public sealed record ProgramLineExecutedEvent(
        int LineNumber,
        DateTime TimestampUtc) : SimulationEvent(TimestampUtc);

    public sealed record WorkOffsetSnapshot(
        int CoordinateSystemNumber,
        double X,
        double Y,
        double Z);

    public sealed record WorkOffsetsChangedEvent(
        IReadOnlyList<WorkOffsetSnapshot> Offsets,
        DateTime TimestampUtc) : SimulationEvent(TimestampUtc);
}
