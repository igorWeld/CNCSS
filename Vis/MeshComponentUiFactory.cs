using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Builds WPF <see cref="MeshComponent"/> instances on the UI thread.</summary>
    internal static class MeshComponentUiFactory
    {
        public static IReadOnlyList<MeshComponent> FromStlExports(IReadOnlyList<CadComponentStlExport> exports)
        {
            Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (!dispatcher.CheckAccess())
            {
                return dispatcher.Invoke(() => Materialize(exports));
            }

            return Materialize(exports);
        }

        private static List<MeshComponent> Materialize(IReadOnlyList<CadComponentStlExport> exports)
        {
            var components = new List<MeshComponent>(exports.Count);
            foreach (CadComponentStlExport export in exports)
            {
                try
                {
                    if (!File.Exists(export.TempStlPath))
                    {
                        continue;
                    }

                    Model3D? model = new StLReader().Read(export.TempStlPath);
                    if (model == null)
                    {
                        continue;
                    }

                    components.Add(new MeshComponent
                    {
                        Index = export.Index,
                        DisplayName = export.DisplayName,
                        Model = model,
                        TriangleCount = CountTriangles(model)
                    });
                }
                finally
                {
                    MeshComponentFileExporter.TryDelete(export.TempStlPath);
                }
            }

            return components;
        }

        private static int CountTriangles(Model3D model)
        {
            if (model is GeometryModel3D geom && geom.Geometry is MeshGeometry3D mesh)
            {
                return mesh.TriangleIndices.Count / 3;
            }

            if (model is Model3DGroup group)
            {
                return group.Children.Sum(CountTriangles);
            }

            return 0;
        }
    }
}
