using System.Collections.Concurrent;
using System.Windows.Media.Media3D;
using CNCSS.Data;

namespace CNCSS.Machine.Model
{
    /// <summary>Точка крепления заготовки и координаты на столе.</summary>
    public static class WorkpieceMountPlacement
    {
        private static readonly ConcurrentDictionary<int, Point3D> WcsOriginTableLocalBySystem = new();

        /// <summary>Фиксированный ноль WCS в СК <c>table.Root</c> (угол заготовки и т.п.).</summary>
        public static void RegisterWcsOriginTableLocal(int systemNumber, Point3D tableRootLocal) =>
            WcsOriginTableLocalBySystem[systemNumber] = tableRootLocal;

        public static void ClearWcsOriginTableLocalCache() => WcsOriginTableLocalBySystem.Clear();

        public static bool TryGetRegisteredWcsOriginTableLocal(int systemNumber, out Point3D tableRootLocal) =>
            WcsOriginTableLocalBySystem.TryGetValue(systemNumber, out tableRootLocal);

        /// <summary>Точка в MCS → table.Root local при заданной позе осей.</summary>
        public static Point3D WcsMcsToTableRootLocal(
            MachineDefinition definition,
            double machineX,
            double machineY,
            double machineZ,
            Point3D wcsMcs) =>
            TableSceneTransforms.TcpMcsToTableRootLocal(definition, machineX, machineY, machineZ, wcsMcs);

        /// <summary>table.Root local → MCS при заданной позе осей.</summary>
        public static Point3D TableRootLocalToWcsMcs(
            MachineDefinition definition,
            double machineX,
            double machineY,
            double machineZ,
            Point3D tableRootLocal)
        {
            Matrix3D kinematic = TableSceneTransforms.BuildTableKinematicMatrix(definition, machineX, machineY, machineZ);
            return kinematic.Transform(tableRootLocal);
        }
        public static MachineGeometryPoint ResolveMountLocal(
            MachineDefinition definition,
            Rect3D? tableMeshBoundsLocal) =>
            WorkpieceMountHelper.ResolveMountLocal(definition.WorkpieceMount, tableMeshBoundsLocal);

        public static Point3D MountLocalToTableNodeFrame(MachineDefinition definition, MachineGeometryPoint mountLocal)
        {
            Transform3D meshLocal = MachineNodeMeshTransforms.BuildMeshLocalTransform(definition.Table);
            return meshLocal.Transform(new Point3D(mountLocal.X, mountLocal.Y, mountLocal.Z));
        }

        public static Point3D MountLocalToTableNodeFrame(
            MachineDefinition definition,
            Rect3D? tableMeshBoundsLocal)
        {
            MachineGeometryPoint mountLocal = ResolveMountLocal(definition, tableMeshBoundsLocal);
            return MountLocalToTableNodeFrame(definition, mountLocal);
        }

        public static Point3D ComputeMountOnKinematicTable(
            MachineDefinition definition,
            IReadOnlyDictionary<string, Transform3D> transforms,
            Rect3D? tableMeshBoundsLocal = null)
        {
            Point3D inNode = MountLocalToTableNodeFrame(definition, tableMeshBoundsLocal);
            if (!transforms.TryGetValue(MachineNodeIds.Table, out Transform3D? kinematic))
            {
                kinematic = Transform3D.Identity;
            }

            return MachineNodeMeshTransforms.TransformPoint(kinematic, inNode);
        }

        public static Point3D ComputeMountWorldWithFixture(
            MachineDefinition definition,
            IReadOnlyDictionary<string, Transform3D> transforms,
            Rect3D? tableMeshBoundsLocal = null)
        {
            Point3D onTable = ComputeMountOnKinematicTable(definition, transforms, tableMeshBoundsLocal);
            return new Point3D(onTable.X, onTable.Y, onTable.Z + definition.FixtureHeightMm);
        }

        /// <summary>Точка WCS (MCS) → абсолютная сцена (мм) с учётом <see cref="MachineDefinition.McsZeroOffset"/>.</summary>
        public static Point3D GetWcsOriginScenePoint(
            MachineDefinition definition,
            double wcsMcsX,
            double wcsMcsY,
            double wcsMcsZ)
        {
            MachineGeometryPoint mcs = definition.McsZeroOffset ?? MachineGeometryPoint.Zero;
            return new Point3D(
                wcsMcsX + mcs.X,
                wcsMcsY + mcs.Y,
                wcsMcsZ + mcs.Z);
        }

        /// <summary>Смещение активного G54…G59 в MCS (как после «Быстрой установки WCS»).</summary>
        public static Point3D GetActiveWorkOffsetMcsPoint(MachineState state)
        {
            MachineState.WorkOffset wcs = state.GetActiveWorkOffset();
            return new Point3D(wcs.X, wcs.Y, wcs.Z);
        }

        /// <summary>Физическая поза осей при программе (0,0,0) в активном WCS.</summary>
        public static (double X, double Y, double Z) GetProgramZeroPhysical(MachineState state)
        {
            MachineState.WorkOffset wcs = state.GetActiveWorkOffset();
            return (
                wcs.X + state.MachineZeroOffsetX,
                wcs.Y + state.MachineZeroOffsetY,
                wcs.Z + state.MachineZeroOffsetZ);
        }

        /// <summary>
        /// Ноль WCS на столе: зарегистрированный table-local или захват при текущей позе осей.
        /// </summary>
        public static Point3D ResolveFixedWcsOriginTableLocal(
            MachineDefinition definition,
            int systemNumber,
            double wcsMcsX,
            double wcsMcsY,
            double wcsMcsZ,
            double machineX,
            double machineY,
            double machineZ,
            bool captureIfMissing = true)
        {
            if (TryGetRegisteredWcsOriginTableLocal(systemNumber, out Point3D tableRootLocal))
            {
                return tableRootLocal;
            }

            Point3D captured = WcsMcsToTableRootLocal(
                definition,
                machineX,
                machineY,
                machineZ,
                new Point3D(wcsMcsX, wcsMcsY, wcsMcsZ));
            if (captureIfMissing)
            {
                RegisterWcsOriginTableLocal(systemNumber, captured);
            }

            return captured;
        }

        /// <summary>
        /// Ноль WCS (G54…G59) в СК <c>table.Root</c> для заданного смещения в MCS.
        /// </summary>
        public static Point3D GetWcsOriginTableRootForSystem(
            MachineDefinition definition,
            int systemNumber,
            double wcsMcsX,
            double wcsMcsY,
            double wcsMcsZ,
            double machineX,
            double machineY,
            double machineZ,
            double toolStickOutMm = 0)
        {
            _ = toolStickOutMm;
            if (TryGetRegisteredWcsOriginTableLocal(systemNumber, out Point3D tableRootLocal))
            {
                return tableRootLocal;
            }

            return WcsMcsToTableRootLocal(
                definition,
                machineX,
                machineY,
                machineZ,
                new Point3D(wcsMcsX, wcsMcsY, wcsMcsZ));
        }

        /// <summary>
        /// Ноль активного WCS в СК <c>table.Root</c> (фиксирован на столе).
        /// </summary>
        public static Point3D GetWcsOriginTableRoot(
            MachineDefinition definition,
            MachineState state,
            double machineX,
            double machineY,
            double machineZ,
            double toolStickOutMm = 0)
        {
            MachineState.WorkOffset wcs = state.GetActiveWorkOffset();
            return GetWcsOriginTableRootForSystem(
                definition,
                state.CurrentCoordinateSystem.Number,
                wcs.X,
                wcs.Y,
                wcs.Z,
                machineX,
                machineY,
                machineZ,
                toolStickOutMm);
        }

        /// <summary>Зафиксировать ноль WCS на столе при текущей позе осей (MCS → table.Root).</summary>
        public static void CaptureWcsOriginOnTableFromMcs(
            MachineDefinition definition,
            int systemNumber,
            double wcsMcsX,
            double wcsMcsY,
            double wcsMcsZ,
            double machineX,
            double machineY,
            double machineZ) =>
            RegisterWcsOriginTableLocal(
                systemNumber,
                WcsMcsToTableRootLocal(
                    definition,
                    machineX,
                    machineY,
                    machineZ,
                    new Point3D(wcsMcsX, wcsMcsY, wcsMcsZ)));

        /// <summary>Пересчитать table-local нули WCS из значений G54…G59 в MCS при текущей позе осей.</summary>
        public static void SyncWcsOriginTableLocalFromMcs(
            MachineDefinition definition,
            IReadOnlyDictionary<int, (double X, double Y, double Z)> offsetsMcs,
            double machineX,
            double machineY,
            double machineZ)
        {
            WcsOriginTableLocalBySystem.Clear();
            foreach (KeyValuePair<int, (double X, double Y, double Z)> pair in offsetsMcs)
            {
                if (pair.Key is < MachineState.MinWorkOffsetNumber or > MachineState.MaxWorkOffsetNumber)
                {
                    continue;
                }

                Point3D tableLocal = WcsMcsToTableRootLocal(
                    definition,
                    machineX,
                    machineY,
                    machineZ,
                    new Point3D(pair.Value.X, pair.Value.Y, pair.Value.Z));
                RegisterWcsOriginTableLocal(pair.Key, tableLocal);
            }
        }

        /// <summary>WCS в MCS при текущей позе осей (для маркера на корне сборки).</summary>
        public static Point3D GetWcsOriginAssemblyMcs(
            MachineDefinition definition,
            MachineState state,
            double machineX,
            double machineY,
            double machineZ,
            double toolStickOutMm = 0)
        {
            Point3D wcsOnTable = GetWcsOriginTableRoot(definition, state, machineX, machineY, machineZ, toolStickOutMm);
            Matrix3D kinematic = TableSceneTransforms.BuildTableKinematicMatrix(
                definition,
                machineX,
                machineY,
                machineZ);
            return kinematic.Transform(wcsOnTable);
        }

        /// <summary>
        /// Физическая поза осей → table.Root: ноль WCS зафиксирован на столе + координаты программы.
        /// Не зависит от TCP, вылета инструмента и текущей позы осей (только от G54…G59).
        /// </summary>
        public static Point3D PhysicalProgramToTableLocal(
            MachineDefinition definition,
            MachineState state,
            double physicalX,
            double physicalY,
            double physicalZ)
        {
            // Контур УП — в WCS по кончику; G43 смещает ось Z, а не запрограммированный контур.
            state.MachineAxisToWorkpieceTip(physicalX, physicalY, physicalZ, out double programX, out double programY, out double programZ);
            MachineState.WorkOffset wcs = state.GetActiveWorkOffset();

            int systemNumber = state.CurrentCoordinateSystem.Number;
            if (!TryGetRegisteredWcsOriginTableLocal(systemNumber, out Point3D wcsOrigin))
            {
                wcsOrigin = ResolveFixedWcsOriginTableLocal(
                    definition,
                    systemNumber,
                    wcs.X,
                    wcs.Y,
                    wcs.Z,
                    state.X,
                    state.Y,
                    state.Z,
                    captureIfMissing: true);
            }

            return new Point3D(
                wcsOrigin.X + programX,
                wcsOrigin.Y + programY,
                wcsOrigin.Z + programZ);
        }

        /// <summary>
        /// Точка УП в СК <c>table.Root</c>: начало контура в нуле WCS, далее координаты программы.
        /// </summary>
        public static Point3D ProgramPhysicalToTableLocal(
            MachineDefinition definition,
            MachineState state,
            double physicalX,
            double physicalY,
            double physicalZ,
            double toolStickOutMm = 0,
            double? anchorMachineX = null,
            double? anchorMachineY = null,
            double? anchorMachineZ = null)
        {
            _ = toolStickOutMm;
            _ = anchorMachineX;
            _ = anchorMachineY;
            _ = anchorMachineZ;
            if (MachineKinematics.UsesTableMountedWorkpiece(definition))
            {
                return PhysicalProgramToTableLocal(definition, state, physicalX, physicalY, physicalZ);
            }

            state.MachineAxisToWorkpieceTip(physicalX, physicalY, physicalZ, out double programX, out double programY, out double programZ);
            MachineState.WorkOffset wcs = state.GetActiveWorkOffset();
            Point3D wcsScene = GetWcsOriginScenePoint(definition, wcs.X, wcs.Y, wcs.Z);
            return new Point3D(
                wcsScene.X + programX,
                wcsScene.Y + programY,
                wcsScene.Z + programZ);
        }

        public static WorkpiecePlacement.StockBounds AlignStockTableLocal(
            MachineDefinition definition,
            Rect3D? tableMeshBoundsLocal,
            double width,
            double depth,
            double height)
        {
            Point3D mountNode = MountLocalToTableNodeFrame(definition, tableMeshBoundsLocal);
            return WorkpiecePlacement.AlignToMountTableLocal(
                new MachineGeometryPoint { X = mountNode.X, Y = mountNode.Y, Z = mountNode.Z },
                width,
                depth,
                height,
                definition.FixtureHeightMm);
        }
    }
}
