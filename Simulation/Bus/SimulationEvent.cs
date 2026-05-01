namespace CNCSS.Simulation.Bus
{
    /// <summary>Базовая запись для всех событий симуляции (шина времени и контроллера).</summary>
    /// <param name="TimestampUtc">Время события в UTC.</param>
    public abstract record SimulationEvent(DateTime TimestampUtc);
}
