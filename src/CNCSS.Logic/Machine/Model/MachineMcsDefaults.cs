namespace CNCSS.Machine.Model
{
    /// <summary>Известные положения маркера MCS для поставляемых профилей станка (мм, мировые координаты превью).</summary>
    public static class MachineMcsDefaults
    {
        /// <summary>
        /// Торец шпинделя для сборки <c>Default_Maschine.stp</c> после импорта и «Переместить узлы в MCS» (HOME = 0 в MCS).
        /// </summary>
        public static MachineGeometryPoint DefaultBundledSpindleFaceMcsMarker { get; } = new()
        {
            X = -0.366,
            Y = -110.882,
            Z = 880
        };
    }
}
