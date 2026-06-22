using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.UI;
using CNCSS.Machine.Configuration;
using CNCSS.Machine.Model;
using HelixToolkit.Wpf;

namespace CNCSS.Vis
{
    /// <summary>
    /// Параметрические меши заготовки для превью конструктора и для отображения на станке до Cycle Start.
    /// Формы: прямоугольник, шестигранник (Param1 — размер по плоскостям), круг, труба (100/30/50 по умолчанию).
    /// </summary>
    public static class StockConstructorPreviewBuilder
    {
        public static Model3D BuildTableModel(Model3D? tableModel)
        {
            if (tableModel != null)
            {
                // Меш стола из профиля станка (клонирование выполняет вызывающий код).
                return tableModel;
            }

            var mb = new MeshBuilder(false, false);
            // Запасной стол: плоскость вокруг начала координат, Z = 0.
            mb.AddBox(new Point3D(0, 0, -2), 420, 320, 4);
            MeshGeometry3D mesh = mb.ToMesh()!;
            mesh.Freeze();

            var brush = new SolidColorBrush(Color.FromArgb(255, 235, 235, 235));
            brush.Freeze();
            Material material = MaterialHelper.CreateMaterial(brush);

            var gm = new GeometryModel3D(mesh, material) { BackMaterial = material };
            gm.Freeze();
            return gm;
        }

        public static Model3D BuildStockPreviewModel(StockConstructorConfig config)
        {
            MeshGeometry3D mesh = BuildStockMesh(config);
            mesh.Freeze();

            // Полупрозрачное превью — видно положение относительно стола.
            Color c = config.Color.ToWpfColor();
            var brush = new SolidColorBrush(Color.FromArgb(170, c.R, c.G, c.B));
            brush.Freeze();
            Material mat = MaterialHelper.CreateMaterial(brush);

            var gm = new GeometryModel3D(mesh, mat) { BackMaterial = mat };
            gm.Freeze();
            return gm;
        }

        public static bool TryBuildStockPreviewModelOnMount(
            MachineDefinition definition,
            Rect3D? tableMeshBoundsLocal,
            double machineX,
            double machineY,
            double machineZ,
            StockConstructorConfig config,
            out Model3D? model)
        {
            model = null;

            if (!TryBuildStockBoundsTableLocal(definition, tableMeshBoundsLocal, machineX, machineY, machineZ, config, out WorkpiecePlacement.StockBounds bounds))
            {
                return false;
            }

            MeshGeometry3D mesh = BuildStockMeshFromBounds(config, bounds);
            mesh.Freeze();

            Color c = config.Color.ToWpfColor();
            var brush = new SolidColorBrush(Color.FromArgb(170, c.R, c.G, c.B));
            brush.Freeze();
            Material mat = MaterialHelper.CreateMaterial(brush);
            var gm = new GeometryModel3D(mesh, mat) { BackMaterial = mat };
            gm.Freeze();
            model = gm;
            return true;
        }

        private static bool TryBuildStockBoundsTableLocal(
            MachineDefinition profile,
            Rect3D? tableMeshBoundsLocal,
            double machineX,
            double machineY,
            double machineZ,
            StockConstructorConfig cfg,
            out WorkpiecePlacement.StockBounds bounds)
        {
            bounds = default;

            (double width, double depth, double height) dims = cfg.ShapeType switch
            {
                StockShapeType.Rectangular => (cfg.Param1Mm, cfg.Param2Mm, cfg.Param3Mm),
                // Шестигранник: Param1 — размер по плоскостям (диаметр вписанной окружности), Param2 — толщина по Z.
                StockShapeType.Hexagonal => (cfg.Param1Mm, cfg.Param1Mm, cfg.Param2Mm),
                StockShapeType.Round => (cfg.Param1Mm, cfg.Param1Mm, cfg.Param2Mm),
                StockShapeType.Tube => (cfg.Param1Mm, cfg.Param1Mm, cfg.Param3Mm),
                _ => default
            };

            if (dims.width <= 0 || dims.depth <= 0 || dims.height <= 0)
            {
                return false;
            }

            WorkpiecePlacement.StockBounds aligned = WorkpieceMountPlacement.AlignStockTableLocal(
                profile,
                tableMeshBoundsLocal,
                dims.width,
                dims.depth,
                dims.height);

            var centerTable = new Point3D(
                (aligned.MinX + aligned.MaxX) * 0.5,
                (aligned.MinY + aligned.MaxY) * 0.5,
                (aligned.MinZ + aligned.MaxZ) * 0.5);

            Point3D centerMcs = WorkpieceMountPlacement.TableRootLocalToWcsMcs(profile, machineX, machineY, machineZ, centerTable);
            var desiredCenterMcs = new Point3D(cfg.CenterXMcsMm, cfg.CenterYMcsMm, centerMcs.Z);
            Point3D desiredCenterTable = WorkpieceMountPlacement.WcsMcsToTableRootLocal(profile, machineX, machineY, machineZ, desiredCenterMcs);

            Vector3D delta = desiredCenterTable - centerTable;
            bounds = new WorkpiecePlacement.StockBounds(
                aligned.MinX + delta.X,
                aligned.MaxX + delta.X,
                aligned.MinY + delta.Y,
                aligned.MaxY + delta.Y,
                aligned.MinZ,
                aligned.MaxZ);
            return true;
        }

        public static MeshGeometry3D BuildStockMeshFromBounds(StockConstructorConfig config, WorkpiecePlacement.StockBounds bounds)
        {
            double cx = (bounds.MinX + bounds.MaxX) * 0.5;
            double cy = (bounds.MinY + bounds.MaxY) * 0.5;
            double minZ = bounds.MinZ;
            double maxZ = bounds.MaxZ;
            double height = Math.Max(1e-6, maxZ - minZ);

            switch (config.ShapeType)
            {
                case StockShapeType.Rectangular:
                {
                    var mb = new MeshBuilder(false, false);
                    mb.AddBox(
                        new Point3D(cx, cy, (minZ + maxZ) * 0.5),
                        Math.Max(1e-6, config.Param1Mm),
                        Math.Max(1e-6, config.Param2Mm),
                        height);
                    return mb.ToMesh()!;
                }
                case StockShapeType.Round:
                {
                    double r = Math.Max(1e-6, config.Param1Mm * 0.5);
                    var mb = new MeshBuilder(false, false);
                    mb.AddCylinder(new Point3D(cx, cy, minZ), new Point3D(cx, cy, maxZ), r, 48, true, true);
                    return mb.ToMesh()!;
                }
                case StockShapeType.Tube:
                {
                    double rOuter = Math.Max(1e-6, config.Param1Mm * 0.5);
                    double rInner = Math.Max(1e-6, config.Param2Mm * 0.5);
                    var mb = new MeshBuilder(false, false);
                    mb.AddPipe(new Point3D(cx, cy, minZ), new Point3D(cx, cy, maxZ), rInner * 2, rOuter * 2, 48);
                    return mb.ToMesh()!;
                }
                case StockShapeType.Hexagonal:
                {
                    MeshGeometry3D mesh = BuildHexPrism(cx, cy, config.Param1Mm, height);
                    // Сдвиг призмы к нижней грани bounds (minZ).
                    var positions = new Point3DCollection(mesh.Positions.Count);
                    foreach (Point3D p in mesh.Positions)
                    {
                        positions.Add(new Point3D(p.X, p.Y, p.Z + minZ));
                    }
                    mesh.Positions = positions;
                    return mesh;
                }
                default:
                    return new MeshGeometry3D();
            }
        }

        private static MeshGeometry3D BuildStockMesh(StockConstructorConfig config)
        {
            double cx = config.CenterXMcsMm;
            double cy = config.CenterYMcsMm;

            switch (config.ShapeType)
            {
                case StockShapeType.Rectangular:
                {
                    double len = config.Param1Mm;
                    double wid = config.Param2Mm;
                    double th = config.Param3Mm;
                    return BuildBox(cx, cy, len, wid, th);
                }
                case StockShapeType.Hexagonal:
                {
                    double acrossFlats = config.Param1Mm;
                    double th = config.Param2Mm;
                    return BuildHexPrism(cx, cy, acrossFlats, th);
                }
                case StockShapeType.Round:
                {
                    double dia = config.Param1Mm;
                    double th = config.Param2Mm;
                    return BuildCylinder(cx, cy, dia, th);
                }
                case StockShapeType.Tube:
                {
                    double dOuter = config.Param1Mm;
                    double dHole = config.Param2Mm;
                    double th = config.Param3Mm;
                    return BuildTube(cx, cy, dOuter, dHole, th);
                }
                default:
                    return new MeshGeometry3D();
            }
        }

        private static MeshGeometry3D BuildBox(double cx, double cy, double len, double wid, double th)
        {
            var mb = new MeshBuilder(false, false);
            mb.AddBox(new Point3D(cx, cy, th * 0.5), len, wid, th);
            return mb.ToMesh()!;
        }

        private static MeshGeometry3D BuildCylinder(double cx, double cy, double diameter, double th)
        {
            double r = Math.Max(1e-6, diameter * 0.5);
            var mb = new MeshBuilder(false, false);
            mb.AddCylinder(new Point3D(cx, cy, 0), new Point3D(cx, cy, th), r, 48, true, true);
            return mb.ToMesh()!;
        }

        private static MeshGeometry3D BuildTube(double cx, double cy, double outerDiameter, double holeDiameter, double th)
        {
            double rOuter = Math.Max(1e-6, outerDiameter * 0.5);
            double rInner = Math.Max(1e-6, holeDiameter * 0.5);
            var mb = new MeshBuilder(false, false);
            // Helix WPF: AddPipe(p1, p2, innerDiameter, outerDiameter, thetaDiv).
            mb.AddPipe(new Point3D(cx, cy, 0), new Point3D(cx, cy, th), rInner * 2, rOuter * 2, 48);
            return mb.ToMesh()!;
        }

        private static MeshGeometry3D BuildHexPrism(double cx, double cy, double acrossFlats, double th)
        {
            // Правильный шестигранник: acrossFlats — диаметр вписанной окружности (2×apothem).
            // Призму строим явно — перегрузка extrude с совпадающими точками даёт пустой меш.
            double apothem = Math.Max(1e-6, acrossFlats * 0.5);
            IList<Point> section = CreateHexSection(apothem);

            var positions = new Point3DCollection(6 + 6 + 2);
            for (int i = 0; i < section.Count; i++)
            {
                positions.Add(new Point3D(cx + section[i].X, cy + section[i].Y, 0));
            }
            for (int i = 0; i < section.Count; i++)
            {
                positions.Add(new Point3D(cx + section[i].X, cy + section[i].Y, th));
            }

            int bottomCenterIndex = positions.Count;
            positions.Add(new Point3D(cx, cy, 0));
            int topCenterIndex = positions.Count;
            positions.Add(new Point3D(cx, cy, th));

            var indices = new Int32Collection();

            // Нижняя крышка (нормаль вниз): центр, следующая, текущая.
            for (int i = 0; i < 6; i++)
            {
                int i0 = i;
                int i1 = (i + 1) % 6;
                indices.Add(bottomCenterIndex);
                indices.Add(i1);
                indices.Add(i0);
            }

            // Верхняя крышка (нормаль вверх): центр, текущая, следующая.
            for (int i = 0; i < 6; i++)
            {
                int i0 = 6 + i;
                int i1 = 6 + ((i + 1) % 6);
                indices.Add(topCenterIndex);
                indices.Add(i0);
                indices.Add(i1);
            }

            // Боковые грани.
            for (int i = 0; i < 6; i++)
            {
                int b0 = i;
                int b1 = (i + 1) % 6;
                int t0 = 6 + i;
                int t1 = 6 + ((i + 1) % 6);

                indices.Add(b0);
                indices.Add(b1);
                indices.Add(t1);

                indices.Add(b0);
                indices.Add(t1);
                indices.Add(t0);
            }

            return new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = indices
            };
        }

        private static IList<Point> CreateHexSection(double apothem)
        {
            // Шестигранник в начале координат; радиус вершин: r = apothem / cos(30°).
            double r = apothem / Math.Cos(Math.PI / 6.0);
            var pts = new List<Point>(6);
            for (int i = 0; i < 6; i++)
            {
                double a = (Math.PI / 3.0) * i + Math.PI / 6.0;
                pts.Add(new Point(Math.Cos(a) * r, Math.Sin(a) * r));
            }
            return pts;
        }
    }
}

