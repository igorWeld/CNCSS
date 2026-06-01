using System.Collections.Concurrent;

namespace CNCSS.Vis
{
    /// <summary>
    /// Serializes all Open CASCADE / Occt.NET calls on one dedicated STA thread.
    /// Occt.NET is not safe from arbitrary thread-pool threads and may show native error dialogs on failure.
    /// </summary>
    internal static class OcctCadThreadRunner
    {
        private static readonly object Gate = new();
        private static Thread? _thread;
        private static BlockingCollection<WorkItem>? _queue;
        private static int _started;

        public static Task<T> RunAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(func);
            EnsureStarted();

            if (Thread.CurrentThread == _thread)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(func());
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var item = new WorkItem
            {
                Run = () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return func();
                },
                SetResult = o => tcs.TrySetResult((T)o!),
                SetException = ex => tcs.TrySetException(ex),
                SetCanceled = () => tcs.TrySetCanceled(cancellationToken)
            };

            if (!_queue!.TryAdd(item))
            {
                tcs.TrySetException(new ObjectDisposedException(nameof(OcctCadThreadRunner)));
                return tcs.Task;
            }

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    if (tcs.TrySetCanceled(cancellationToken))
                    {
                        item.CancelRequested = true;
                    }
                });
            }

            return tcs.Task;
        }

        private static void EnsureStarted()
        {
            if (Volatile.Read(ref _started) == 1)
            {
                return;
            }

            lock (Gate)
            {
                if (_started == 1)
                {
                    return;
                }

                _queue = new BlockingCollection<WorkItem>();
                _thread = new Thread(ThreadMain)
                {
                    Name = "CNCSS Occt CAD",
                    IsBackground = true
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                Volatile.Write(ref _started, 1);
            }
        }

        private static void ThreadMain()
        {
            _ = OcctNativeLoader.EnsureInitialized();

            try
            {
                foreach (WorkItem item in _queue!.GetConsumingEnumerable())
                {
                    if (item.CancelRequested)
                    {
                        item.SetCanceled();
                        continue;
                    }

                    try
                    {
                        object result = item.Run();
                        item.SetResult(result);
                    }
                    catch (OperationCanceledException)
                    {
                        item.SetCanceled();
                    }
                    catch (Exception ex)
                    {
                        item.SetException(ex);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private sealed class WorkItem
        {
            public required Func<object> Run { get; init; }

            public required Action<object> SetResult { get; init; }

            public required Action<Exception> SetException { get; init; }

            public required Action SetCanceled { get; init; }

            public bool CancelRequested { get; set; }
        }
    }
}
