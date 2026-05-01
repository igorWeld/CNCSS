namespace CNCSS.Simulation.Bus
{
    public interface ISimulationBus
    {
        void Publish<TEvent>(TEvent evt) where TEvent : SimulationEvent;
        IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : SimulationEvent;
    }
}
