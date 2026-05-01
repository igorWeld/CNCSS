namespace CNCSS.Simulation.Bus
{
    /// <summary>
    /// Потокобезопасная реализация <see cref="ISimulationBus"/> с хранением делегатов подписчиков по типу события.
    /// </summary>
    public sealed class SimulationBus : ISimulationBus
    {
        private readonly Dictionary<Type, Delegate[]> _subscriptions = new();
        private readonly object _sync = new();

        public void Publish<TEvent>(TEvent evt) where TEvent : SimulationEvent
        {
            Delegate[]? handlers;
            lock (_sync)
            {
                if (!_subscriptions.TryGetValue(typeof(TEvent), out handlers))
                {
                    return;
                }
            }

            foreach (var handler in handlers)
            {
                ((Action<TEvent>)handler)(evt);
            }
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : SimulationEvent
        {
            lock (_sync)
            {
                if (!_subscriptions.TryGetValue(typeof(TEvent), out var handlers))
                {
                    _subscriptions[typeof(TEvent)] = new Delegate[] { handler };
                }
                else
                {
                    var copy = new Delegate[handlers.Length + 1];
                    Array.Copy(handlers, copy, handlers.Length);
                    copy[^1] = handler;
                    _subscriptions[typeof(TEvent)] = copy;
                }
            }

            return new Subscription(() => Unsubscribe(handler));
        }

        private void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : SimulationEvent
        {
            lock (_sync)
            {
                if (!_subscriptions.TryGetValue(typeof(TEvent), out var handlers))
                {
                    return;
                }

                int index = Array.IndexOf(handlers, handler);
                if (index < 0)
                {
                    return;
                }

                if (handlers.Length == 1)
                {
                    _subscriptions.Remove(typeof(TEvent));
                    return;
                }

                var copy = new Delegate[handlers.Length - 1];
                if (index > 0)
                {
                    Array.Copy(handlers, 0, copy, 0, index);
                }
                if (index < handlers.Length - 1)
                {
                    Array.Copy(handlers, index + 1, copy, index, handlers.Length - index - 1);
                }

                _subscriptions[typeof(TEvent)] = copy;
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action _unsubscribe;
            private bool _disposed;

            public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _unsubscribe();
                _disposed = true;
            }
        }
    }
}
