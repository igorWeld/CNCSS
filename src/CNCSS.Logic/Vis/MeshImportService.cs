using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>Imports mesh files and splits multi-body assemblies into components.</summary>
    public sealed class MeshImportService
    {
        /// <summary>Mesh quality for STEP/IGES import via Open CASCADE.</summary>
        public CadMeshQualitySettings StepMeshQuality { get; set; } = CadMeshQualitySettings.Standard;

        public async Task<MeshImportResult> ImportAsync(string fullPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                return MeshImportResult.Failed(fullPath ?? string.Empty, "Файл не найден.");
            }

            if (!MeshImportFormats.IsSupported(fullPath))
            {
                return MeshImportResult.Failed(fullPath, "Поддерживаются форматы STL, OBJ, STEP (.stp), IGES (.igs).");
            }

            Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            if (MeshImportFormats.IsAssimpPath(fullPath))
            {
                if (!OcctNativeLoader.EnsureInitialized())
                {
                    string detail = OcctNativeLoader.InitializationError
                        ?? "не удалось загрузить нативные библиотеки Open CASCADE.";
                    return MeshImportResult.Failed(
                        fullPath,
                        "Импорт STEP/IGES недоступен: " + detail
                        + Environment.NewLine
                        + "Проверьте, что рядом с CNCSS.exe есть папка occt\\x64, и установлен Visual C++ Redistributable 2015–2022 (x64).");
                }

                CadMeshQualitySettings meshQuality = StepMeshQuality;
                IReadOnlyList<CadComponentStlExport> exports = await OcctCadThreadRunner.RunAsync(
                    () => OcctCadModelConverter.ExportComponentsToStl(fullPath, meshQuality),
                    cancellationToken);

                return await dispatcher.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return BuildCadImportResult(fullPath, exports);
                }, DispatcherPriority.Background, cancellationToken);
            }

            return await dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ImportMeshFile(fullPath);
            }, DispatcherPriority.Background, cancellationToken);
        }

        private static MeshImportResult BuildCadImportResult(string fullPath, IReadOnlyList<CadComponentStlExport> exports)
        {
            try
            {
                IReadOnlyList<MeshComponent> components;
                if (exports.Count > 0)
                {
                    components = MeshComponentUiFactory.FromStlExports(exports);
                }
                else if (!string.IsNullOrWhiteSpace(OcctCadModelConverter.LastError))
                {
                    return MeshImportResult.Failed(fullPath, OcctCadModelConverter.LastError);
                }
                else
                {
                    components = AssimpModelConverter.LoadComponentsAssimpOnly(fullPath);
                }

                if (components.Count == 0)
                {
                    return MeshImportResult.Failed(
                        fullPath,
                        OcctCadModelConverter.LastError
                        ?? "Не удалось загрузить CAD-модель. Проверьте файл или экспортируйте в STL.");
                }

                return MeshImportResult.Ok(fullPath, components);
            }
            catch (Exception ex)
            {
                return MeshImportResult.Failed(fullPath, "Не удалось загрузить модель: " + ex.Message);
            }
        }

        private static MeshImportResult ImportMeshFile(string fullPath)
        {
            try
            {
                string baseName = Path.GetFileNameWithoutExtension(fullPath);
                Model3D? model = MeshImportFormats.IsStlPath(fullPath)
                    ? new StLReader().Read(fullPath)
                    : MeshImportFormats.IsObjPath(fullPath)
                        ? new ObjReader().Read(fullPath)
                        : null;

                IReadOnlyList<MeshComponent> components = MeshComponentSplitter.FromModel(model, baseName);
                if (components.Count == 0)
                {
                    return MeshImportResult.Failed(
                        fullPath,
                        "Не удалось загрузить модель. Проверьте файл или экспортируйте в STL.");
                }

                return MeshImportResult.Ok(fullPath, components);
            }
            catch (Exception ex)
            {
                return MeshImportResult.Failed(fullPath, "Не удалось загрузить модель: " + ex.Message);
            }
        }
    }
}
