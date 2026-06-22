namespace CNCSS.Vis
{
    /// <summary>Controls BRep mesh deflection when tessellating CAD imports (STEP/IGES).</summary>
    public sealed class CadMeshQualitySettings
    {
        public static CadMeshQualitySettings Standard { get; } = ForPreset(CadMeshQualityPreset.Standard);

        public required CadMeshQualityPreset Preset { get; init; }

        public required double MinDeflectionMm { get; init; }

        public required double MaxDeflectionMm { get; init; }

        public required double DeflectionFactorOfDiagonal { get; init; }

        public string DisplayName => Preset switch
        {
            CadMeshQualityPreset.Draft => "Черновик",
            CadMeshQualityPreset.Standard => "Стандарт",
            CadMeshQualityPreset.Fine => "Высокое",
            CadMeshQualityPreset.ExtraFine => "Максимум",
            _ => Preset.ToString()
        };

        public string ShortSummary =>
            $"{DisplayName}: шаг сетки ~{MinDeflectionMm:0.##}–{MaxDeflectionMm:0.##} мм";

        public string Description => Preset switch
        {
            CadMeshQualityPreset.Draft =>
                "Быстрый импорт, грубая сетка. Подходит для проверки сборки и назначения ролей.",
            CadMeshQualityPreset.Standard =>
                "Баланс скорости и детализации. Рекомендуется для большинства станков.",
            CadMeshQualityPreset.Fine =>
                "Детальная сетка, импорт дольше. Для крупных сборок может заметно нагружать ПК.",
            CadMeshQualityPreset.ExtraFine =>
                "Максимальная детализация, долгий импорт и большой объём данных. Только для небольших деталей.",
            _ => string.Empty
        };

        public static CadMeshQualitySettings ForPreset(CadMeshQualityPreset preset) => preset switch
        {
            CadMeshQualityPreset.Draft => new()
            {
                Preset = preset,
                MinDeflectionMm = 0.15,
                MaxDeflectionMm = 10.0,
                DeflectionFactorOfDiagonal = 0.006
            },
            CadMeshQualityPreset.Standard => new()
            {
                Preset = preset,
                MinDeflectionMm = 0.05,
                MaxDeflectionMm = 5.0,
                DeflectionFactorOfDiagonal = 0.002
            },
            CadMeshQualityPreset.Fine => new()
            {
                Preset = preset,
                MinDeflectionMm = 0.02,
                MaxDeflectionMm = 2.0,
                DeflectionFactorOfDiagonal = 0.0008
            },
            CadMeshQualityPreset.ExtraFine => new()
            {
                Preset = preset,
                MinDeflectionMm = 0.01,
                MaxDeflectionMm = 0.5,
                DeflectionFactorOfDiagonal = 0.00025
            },
            _ => Standard
        };

        public double ComputeDeflectionMm(double boundingDiagonalMm)
        {
            if (boundingDiagonalMm < 1e-6)
            {
                return Math.Clamp(0.5, MinDeflectionMm, MaxDeflectionMm);
            }

            return Math.Clamp(
                boundingDiagonalMm * DeflectionFactorOfDiagonal,
                MinDeflectionMm,
                MaxDeflectionMm);
        }
    }
}
