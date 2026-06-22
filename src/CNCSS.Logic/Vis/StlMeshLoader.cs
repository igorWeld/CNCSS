using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Loads STL, OBJ, STEP and IGES files into WPF mesh geometry on the UI dispatcher.</summary>
    public sealed class StlMeshLoader
    {
        private readonly Dictionary<string, (DateTime LastWrite, Model3D Model)> _cache = new(StringComparer.OrdinalIgnoreCase);

        public Task<Model3D?> LoadAsync(string fullPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath) || !MeshImportFormats.IsSupported(fullPath))
            {
                return Task.FromResult<Model3D?>(null);
            }

            DateTime lastWrite = File.GetLastWriteTimeUtc(fullPath);
            Dispatcher dispatcher = ResolveDispatcher();

            if (_cache.TryGetValue(fullPath, out var cached) && cached.LastWrite == lastWrite)
            {
                if (dispatcher.CheckAccess())
                {
                    return Task.FromResult<Model3D?>(cached.Model);
                }

                return dispatcher.InvokeAsync(() => cached.Model, DispatcherPriority.Send, cancellationToken).Task;
            }

            if (dispatcher.CheckAccess())
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<Model3D?>(LoadAndCache(fullPath, lastWrite));
            }

            return dispatcher.InvokeAsync(
                () => LoadAndCache(fullPath, lastWrite),
                DispatcherPriority.Send,
                cancellationToken).Task;
        }

        private Model3D? LoadAndCache(string fullPath, DateTime lastWrite)
        {
            Model3D? model = LoadModelOnUiThread(fullPath);
            if (model != null)
            {
                _cache[fullPath] = (lastWrite, model);
            }

            return model;
        }

        public Task<StlAnalysisResult?> AnalyzeAsync(string fullPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath) || !MeshImportFormats.IsSupported(fullPath))
            {
                return Task.FromResult<StlAnalysisResult?>(null);
            }

            Dispatcher dispatcher = ResolveDispatcher();
            if (dispatcher.CheckAccess())
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(AnalyzeOnUiThread(fullPath));
            }

            return dispatcher.InvokeAsync(
                () => AnalyzeOnUiThread(fullPath),
                DispatcherPriority.Send,
                cancellationToken).Task;
        }

        private static StlAnalysisResult? AnalyzeOnUiThread(string fullPath)
        {
            Model3D? model = LoadModelOnUiThread(fullPath);
            if (model == null)
            {
                return null;
            }

            double sourceExtent = StlModelMetrics.GetMaxExtent(model);
            double targetMm = StlUnitScaleHelper.InferTargetMaxExtentMm(sourceExtent);
            double scale = StlUnitScaleHelper.ComputeMeshScale(sourceExtent, targetMm);
            return new StlAnalysisResult(sourceExtent, targetMm, scale);
        }

        private static Model3D? LoadModelOnUiThread(string fullPath)
        {
            if (MeshImportFormats.IsStlPath(fullPath))
            {
                return new StLReader().Read(fullPath);
            }

            if (MeshImportFormats.IsObjPath(fullPath))
            {
                return new ObjReader().Read(fullPath);
            }

            if (MeshImportFormats.IsAssimpPath(fullPath))
            {
                return AssimpModelConverter.LoadFromFile(fullPath);
            }

            return null;
        }

        public void ClearCache()
        {
            Dispatcher dispatcher = ResolveDispatcher();
            if (dispatcher.CheckAccess())
            {
                _cache.Clear();
                return;
            }

            dispatcher.Invoke(_cache.Clear);
        }

        private static Dispatcher ResolveDispatcher()
        {
            return Application.Current?.Dispatcher
                ?? Dispatcher.CurrentDispatcher;
        }
    }
}
