namespace CNCSS.Simulation.Bus
{
    /// <summary>Событие от контроллера: текстовая команда и необязательная полезная нагрузка.</summary>
    public sealed record ControllerCommandEvent(
        string Command,
        string? Payload,
        DateTime TimestampUtc) : SimulationEvent(TimestampUtc);

    /// <summary>Снимок положения и технологических флагов для обновления UI и геометрии станка.</summary>
    public sealed record MachineStateChangedEvent(
        double X,
        double Y,
        double Z,
        double MachineZeroOffsetX,
        double MachineZeroOffsetY,
        double MachineZeroOffsetZ,
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

    /// <summary>Изменение режима ввода в MDI (например абсолют/инкремент).</summary>
    public sealed record MdiModeChangedEvent(
        string Mode,
        DateTime TimestampUtc) : SimulationEvent(TimestampUtc);

    /// <summary>Выполнена строка активной программы (идентификатор строки в УП).</summary>
    public sealed record ProgramLineExecutedEvent(
        int LineNumber,
        DateTime TimestampUtc) : SimulationEvent(TimestampUtc);

    /// <summary>Один снимок смещения по рабочей системе координат.</summary>
    public sealed record WorkOffsetSnapshot(
        int CoordinateSystemNumber,
        double X,
        double Y,
        double Z);

    /// <summary>Изменился набор G54–G59 (или иных) коррекций заготовки.</summary>
    public sealed record WorkOffsetsChangedEvent(
        IReadOnlyList<WorkOffsetSnapshot> Offsets,
        DateTime TimestampUtc) : SimulationEvent(TimestampUtc);
}
