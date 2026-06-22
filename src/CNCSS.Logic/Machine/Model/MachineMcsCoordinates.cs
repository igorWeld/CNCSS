namespace CNCSS.Machine.Model
{
    /// <summary>
    /// Преобразования между MCS (профиль станка), программными физическими осями и абсолютной сценой.
    /// </summary>
    /// <remarks>
    /// В профиле хранятся: <see cref="MachineDefinition.McsZeroOffset"/>, точки крепления узлов и лимиты осей — в MCS.
    /// Абсолютная координата сцены по оси: положение узла в MCS + смещение MCS + значение по оси в MCS.
    /// Пример (X): MCS в сцене (400,…), стол в MCS (100,…), max по X в MCS = 100 → max в сцене = 100+400+100 = 600.
    /// </remarks>
    public static class MachineMcsCoordinates
    {
        public static MachineGeometryPoint GetMcsZeroOffset(MachineDefinition definition) =>
            definition.McsZeroOffset ?? MachineGeometryPoint.Zero;

        /// <summary>Точка в MCS → абсолютная сцена (мм).</summary>
        public static MachineGeometryPoint McsToScene(
            MachineGeometryPoint pointMcs,
            MachineGeometryPoint mcsZeroOffset) =>
            new()
            {
                X = pointMcs.X + mcsZeroOffset.X,
                Y = pointMcs.Y + mcsZeroOffset.Y,
                Z = pointMcs.Z + mcsZeroOffset.Z
            };

        /// <summary>Абсолютная сцена → MCS.</summary>
        public static MachineGeometryPoint SceneToMcs(
            MachineGeometryPoint pointScene,
            MachineGeometryPoint mcsZeroOffset) =>
            new()
            {
                X = pointScene.X - mcsZeroOffset.X,
                Y = pointScene.Y - mcsZeroOffset.Y,
                Z = pointScene.Z - mcsZeroOffset.Z
            };

        /// <summary>Поза оси в MCS из физической позы парсера/симуляции.</summary>
        public static double PhysicalAxisToMcs(double physical, double mcsZeroOffsetComponent) =>
            physical - mcsZeroOffsetComponent;

        /// <summary>Физическая поза оси для G-кода: MCS + смещение MCS (без статического attach узла).</summary>
        public static double McsAxisToPhysical(double axisMcs, double mcsZeroOffsetComponent) =>
            axisMcs + mcsZeroOffsetComponent;

        /// <summary>Компонента <see cref="MachineNodeDefinition.AttachOnParent"/> по имени оси.</summary>
        public static double GetNodeAttachMcs(MachineNodeDefinition node, string axisName) =>
            axisName.ToUpperInvariant() switch
            {
                "X" => node.AttachOnParent.X,
                "Y" => node.AttachOnParent.Y,
                "Z" => node.AttachOnParent.Z,
                _ => 0
            };

        /// <summary>Attach-on-parent (мм) приводного узла оси в MCS.</summary>
        public static double GetDriverAttachMcs(MachineDefinition definition, string axisName)
        {
            MachineAxisDefinition? axis = definition.Axes.FirstOrDefault(a =>
                a.Name.Equals(axisName, StringComparison.OrdinalIgnoreCase));
            if (axis == null)
            {
                return 0;
            }

            MachineNodeDefinition? node = definition.TryGetBuiltInNode(axis.ResolveDriverNodeId());
            return node == null ? 0 : GetNodeAttachMcs(node, axisName);
        }

        /// <summary>
        /// Абсолютный предел по оси в сцене: attach приводного узла (MCS) + ноль MCS в сцене + лимит оси (MCS).
        /// </summary>
        public static double GetSceneAxisLimit(
            MachineDefinition definition,
            string axisName,
            double axisLimitMcs)
        {
            var mcs = GetMcsZeroOffset(definition);
            double component = axisName.ToUpperInvariant() switch
            {
                "X" => mcs.X,
                "Y" => mcs.Y,
                "Z" => mcs.Z,
                _ => 0
            };
            return GetDriverAttachMcs(definition, axisName) + component + axisLimitMcs;
        }

        /// <summary>Мягкие лимиты для <see cref="Machine.Core.MachineCore"/> (физические координаты G-кода).</summary>
        public static (double Min, double Max) GetProgramPhysicalAxisLimits(
            MachineDefinition definition,
            string axisName)
        {
            MachineAxisDefinition? axis = definition.Axes.FirstOrDefault(a =>
                a.Name.Equals(axisName, StringComparison.OrdinalIgnoreCase));
            if (axis == null)
            {
                return (0, 0);
            }

            var mcs = GetMcsZeroOffset(definition);
            double shift = axisName.ToUpperInvariant() switch
            {
                "X" => mcs.X,
                "Y" => mcs.Y,
                "Z" => mcs.Z,
                _ => 0
            };
            double lo = Math.Min(axis.Min, axis.Max);
            double hi = Math.Max(axis.Min, axis.Max);
            return (lo + shift, hi + shift);
        }
    }
}
