namespace CNCSS.Simulation.Bus
{
    /// <summary>
    /// Простая межкомпонентная шина: публикация событий симуляции и подписки по типу <see cref="SimulationEvent"/>.
    /// </summary>
    public interface ISimulationBus
    {
        /// <summary>Публикует событие всем подписчикам этого типа.</summary>
        void Publish<TEvent>(TEvent evt) where TEvent : SimulationEvent;

        /// <summary>Подписка на события типа <typeparamref name="TEvent"/>; возвращает отписку по <see cref="IDisposable.Dispose"/>.</summary>
        IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : SimulationEvent;
    }
}
