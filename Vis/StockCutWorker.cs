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
        private readonly IStockVolume _stock;
        private readonly double _cutStepMm;
        private readonly Channel<CutRequest> _channel;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _workerTask;

        public StockCutWorker(IStockVolume stock, double cutStepMm)
        {
            _stock = stock ?? throw new ArgumentNullException(nameof(stock));
            _cutStepMm = Math.Max(0.05, cutStepMm);
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
                var reader = _channel.Reader;
                while (await reader.WaitToReadAsync(_cts.Token))
                {
                    while (reader.TryRead(out CutRequest req))
                    {
                        try
                        {
                            ExecuteCut(req);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[StockCutWorker] Cut failed: {ex.Message}");
                        }
                    }
                }
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

            // Крупнее шаг — меньше захватов write-lock в VoxelStock; длинный проход режется профилем (мм).
            double stepDist = Math.Max(req.Radius * 0.15, Math.Max(_cutStepMm, 0.05));
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

