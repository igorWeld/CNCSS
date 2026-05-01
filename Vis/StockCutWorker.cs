using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CNCSS.Vis
{
    /// <summary>
    /// Фоновый потребитель очереди съёма: асинхронно вызывает <see cref="VoxelStock"/>, чтобы не блокировать UI-таймер.
    /// </summary>
    public sealed class StockCutWorker : IDisposable
    {
        private readonly VoxelStock _stock;
        private readonly Channel<CutRequest> _channel;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _workerTask;

        public StockCutWorker(VoxelStock stock)
        {
            _stock = stock ?? throw new ArgumentNullException(nameof(stock));
            _channel = Channel.CreateUnbounded<CutRequest>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            _workerTask = Task.Run(WorkerLoop);
        }

        public void EnqueueCut(Point3D start, Point3D end, double radius, double fluteLength, Color toolColor)
        {
            _channel.Writer.TryWrite(new CutRequest(start, end, radius, fluteLength, toolColor));
        }

        private async Task WorkerLoop()
        {
            try
            {
                await foreach (var req in _channel.Reader.ReadAllAsync(_cts.Token))
                    ExecuteCut(req);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void ExecuteCut(CutRequest req)
        {
            var move = req.End - req.Start;
            double length = move.Length;
            if (length <= 1e-9)
                return;

            double stepDist = Math.Max(0.1, req.Radius);
            int steps = (int)Math.Ceiling(length / stepDist);
            if (steps <= 1)
            {
                _stock.CutCylinder(req.Start, req.End, req.Radius, req.FluteLength, req.ToolColor);
                return;
            }

            for (int i = 1; i <= steps; i++)
            {
                double t0 = (i - 1) / (double)steps;
                double t1 = i / (double)steps;
                var s = req.Start + move * t0;
                var e = req.Start + move * t1;
                _stock.CutCylinder(s, e, req.Radius, req.FluteLength, req.ToolColor);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _channel.Writer.TryComplete();
            try { _workerTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
            _cts.Dispose();
        }

        private readonly record struct CutRequest(Point3D Start, Point3D End, double Radius, double FluteLength, Color ToolColor);
    }
}

