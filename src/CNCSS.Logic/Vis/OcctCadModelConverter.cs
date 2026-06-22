using System.IO;
using System.Runtime.InteropServices;
using Occt;

namespace CNCSS.Vis
{
    /// <summary>Imports STEP (AP203/AP214) and IGES via Open CASCADE (Occt.NET).</summary>
    internal static class OcctCadModelConverter
    {
        public static string? LastError { get; private set; }

        public static bool IsAvailable { get; private set; } = true;

        public static IReadOnlyList<MeshComponent> LoadComponentsFromFile(string fullPath)
        {
            IReadOnlyList<CadComponentStlExport> exports = OcctCadThreadRunner
                .RunAsync(() => ExportComponentsToStl(fullPath, CadMeshQualitySettings.Standard))
                .GetAwaiter()
                .GetResult();
            if (exports.Count == 0)
            {
                return Array.Empty<MeshComponent>();
            }

            return MeshComponentUiFactory.FromStlExports(exports);
        }

        /// <summary>STEP/IGES tessellation to temp STL. Must run on <see cref="OcctCadThreadRunner"/>.</summary>
        public static IReadOnlyList<CadComponentStlExport> ExportComponentsToStl(
            string fullPath,
            CadMeshQualitySettings? meshQuality = null)
        {
            meshQuality ??= CadMeshQualitySettings.Standard;
            LastError = null;
            fullPath = Path.GetFullPath(fullPath);

            if (!File.Exists(fullPath))
            {
                LastError = "Файл не найден.";
                return Array.Empty<CadComponentStlExport>();
            }

            if (!IsAvailable)
            {
                LastError = "Модуль Open CASCADE недоступен.";
                return Array.Empty<CadComponentStlExport>();
            }

            if (!OcctNativeLoader.EnsureInitialized())
            {
                IsAvailable = false;
                LastError = "Open CASCADE: " + (OcctNativeLoader.InitializationError ?? "не удалось загрузить нативные библиотеки.");
                return Array.Empty<CadComponentStlExport>();
            }

            bool isStep = MeshImportFormats.IsStepPath(fullPath);
            bool isIges = MeshImportFormats.IsIgesPath(fullPath);
            if (!isStep && !isIges)
            {
                return Array.Empty<CadComponentStlExport>();
            }

            try
            {
                using OcctLicenseDialogSuppressor dialogSuppressor = OcctLicenseDialogSuppressor.Start();
                IReadOnlyList<TopoDS_Shape> shapes = isStep
                    ? ReadStepShapes(fullPath)
                    : ReadIgesShapes(fullPath);

                if (shapes.Count == 0)
                {
                    LastError ??= "Файл не содержит переводимой твердотельной геометрии (STEP AP203/AP214, IGES).";
                    return Array.Empty<CadComponentStlExport>();
                }

                string baseName = Path.GetFileNameWithoutExtension(fullPath);
                var exports = new List<CadComponentStlExport>(shapes.Count);
                for (int i = 0; i < shapes.Count; i++)
                {
                    CadComponentStlExport? export = ExportShapeToStl(shapes[i], baseName, i, shapes.Count, meshQuality);
                    if (export != null)
                    {
                        exports.Add(export);
                    }
                }

                if (exports.Count == 0)
                {
                    LastError ??= "Не удалось построить сетку из CAD-геометрии.";
                }

                return exports;
            }
            catch (BadImageFormatException)
            {
                IsAvailable = false;
                LastError = OcctNativeLoader.InitializationError
                    ?? "Occt.NET: несовместимая сборка. Используйте x64-сборку и установите Visual C++ Redistributable 2015–2022 (x64).";
                return Array.Empty<CadComponentStlExport>();
            }
            catch (SEHException ex)
            {
                LastError = "Сбой нативного модуля Open CASCADE. Пересоберите приложение и убедитесь, что рядом с CNCSS.exe есть папка occt\\x64. " + ex.Message;
                return Array.Empty<CadComponentStlExport>();
            }
            catch (Exception ex)
            {
                LastError = "Ошибка Open CASCADE: " + ex.Message;
                return Array.Empty<CadComponentStlExport>();
            }
        }

        private static IReadOnlyList<TopoDS_Shape> ReadStepShapes(string fullPath)
        {
            return ExecuteNative(() =>
            {
                var reader = new STEPControl_Reader();
                IFSelect_ReturnStatus status = reader.ReadFile(fullPath);
                if (status != IFSelect_ReturnStatus.IFSelect_RetDone)
                {
                    LastError = "STEP: не удалось прочитать файл (проверьте AP203/AP214).";
                    return Array.Empty<TopoDS_Shape>();
                }

                if (reader.TransferRoots() <= 0)
                {
                    LastError = "STEP: не удалось перевести геометрию в модель OCCT.";
                    return Array.Empty<TopoDS_Shape>();
                }

                return CollectComponentShapes(reader.NbShapes, i => reader.Shape(i), reader.OneShape());
            });
        }

        private static IReadOnlyList<TopoDS_Shape> ReadIgesShapes(string fullPath)
        {
            return ExecuteNative(() =>
            {
                var reader = new IGESControl_Reader();
                IFSelect_ReturnStatus status = reader.ReadFile(fullPath);
                if (status != IFSelect_ReturnStatus.IFSelect_RetDone)
                {
                    LastError = "IGES: не удалось прочитать файл.";
                    return Array.Empty<TopoDS_Shape>();
                }

                if (reader.TransferRoots() <= 0)
                {
                    LastError = "IGES: не удалось перевести геометрию в модель OCCT.";
                    return Array.Empty<TopoDS_Shape>();
                }

                return CollectComponentShapes(reader.NbShapes, i => reader.Shape(i), reader.OneShape());
            });
        }

        private static IReadOnlyList<TopoDS_Shape> ExecuteNative(Func<IReadOnlyList<TopoDS_Shape>> action)
        {
            try
            {
                return action();
            }
            catch (SEHException ex)
            {
                LastError = "Сбой при чтении CAD-файла (Open CASCADE): " + ex.Message;
                return Array.Empty<TopoDS_Shape>();
            }
        }

        private static IReadOnlyList<TopoDS_Shape> CollectComponentShapes(
            int nbShapes,
            Func<int, TopoDS_Shape> shapeAt,
            TopoDS_Shape oneShape)
        {
            if (nbShapes > 1)
            {
                var list = new List<TopoDS_Shape>(nbShapes);
                for (int i = 1; i <= nbShapes; i++)
                {
                    TopoDS_Shape shape = shapeAt(i);
                    if (!shape.IsNull)
                    {
                        list.Add(shape);
                    }
                }

                if (list.Count > 0)
                {
                    return list;
                }
            }

            IReadOnlyList<TopoDS_Shape> solids = CollectSolids(ShapeInAssemblyCoordinates(oneShape));
            if (solids.Count >= 2)
            {
                return solids;
            }

            TopoDS_Shape assemblyShape = ShapeInAssemblyCoordinates(oneShape);
            if (!assemblyShape.IsNull)
            {
                return [assemblyShape];
            }

            return Array.Empty<TopoDS_Shape>();
        }

        private static IReadOnlyList<TopoDS_Shape> CollectSolids(TopoDS_Shape root)
        {
            if (root.IsNull)
            {
                return Array.Empty<TopoDS_Shape>();
            }

            var list = new List<TopoDS_Shape>();
            var explorer = new TopExp_Explorer(root, TopAbs_ShapeEnum.TopAbs_SOLID);
            for (; explorer.More; explorer.Next())
            {
                TopoDS_Shape solid = explorer.Current;
                if (!solid.IsNull)
                {
                    list.Add(solid);
                }
            }

            return list;
        }

        private static CadComponentStlExport? ExportShapeToStl(
            TopoDS_Shape shape,
            string baseName,
            int index,
            int total,
            CadMeshQualitySettings meshQuality)
        {
            if (shape.IsNull)
            {
                return null;
            }

            string displayName = total > 1 ? $"{baseName} ({index + 1})" : baseName;
            try
            {
                shape = ShapeInAssemblyCoordinates(shape);
                double deflection = ComputeMeshDeflection(shape, meshQuality);
                var mesher = new BRepMesh_IncrementalMesh(shape, deflection, false, deflection, false);
                mesher.Perform();

                string tempPath = Path.Combine(Path.GetTempPath(), "cncss_occt_" + Guid.NewGuid().ToString("N") + ".stl");
                new StlAPI_Writer().Write(shape, tempPath);
                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length < 84)
                {
                    MeshComponentFileExporter.TryDelete(tempPath);
                    return null;
                }

                return new CadComponentStlExport
                {
                    Index = index,
                    DisplayName = displayName,
                    TempStlPath = tempPath
                };
            }
            catch (SEHException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static TopoDS_Shape ShapeInAssemblyCoordinates(TopoDS_Shape shape)
        {
            TopLoc_Location location = shape.Location;
            if (location.IsIdentity)
            {
                return shape;
            }

            gp_Trsf trsf = location.Transformation;
            var transform = new BRepBuilderAPI_Transform(shape, trsf, true);
            TopoDS_Shape moved = transform.Shape;
            moved.Location = new TopLoc_Location();
            return moved;
        }

        private static double ComputeMeshDeflection(TopoDS_Shape shape, CadMeshQualitySettings meshQuality)
        {
            var box = new Bnd_Box();
            BRepBndLib.Add(shape, box);
            if (box.IsVoid)
            {
                return meshQuality.ComputeDeflectionMm(0);
            }

            gp_Pnt min = box.CornerMin;
            gp_Pnt max = box.CornerMax;
            double dx = max.X - min.X;
            double dy = max.Y - min.Y;
            double dz = max.Z - min.Z;
            double diagonal = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return meshQuality.ComputeDeflectionMm(diagonal);
        }

    }
}
