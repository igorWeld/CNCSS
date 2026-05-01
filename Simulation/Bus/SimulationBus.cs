using System.Collections.Concurrent;

namespace CNCSS.Simulation.Bus
{
    public sealed class SimulationBus : ISimulationBus
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _subscriptions = new();
        private readonly object _sync = new();

        public void Publish<TEvent>(TEvent evt) where TEvent : SimulationEvent
        {
            List<Delegate>? handlers;
            lock (_sync)
            {
                if (!_subscriptions.TryGetValue(typeof(TEvent), out handlers))
                {
                    return;
                }

                handlers = handlers.ToList();
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
                    handlers = new List<Delegate>();
                    _subscriptions[typeof(TEvent)] = handlers;
                }

                handlers.Add(handler);
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

                handlers.Remove(handler);
                if (handlers.Count == 0)
                {
                    _subscriptions.TryRemove(typeof(TEvent), out _);
                }
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
