using CNCSS.Data;

namespace CNCSS.Data.Config.Enums
{
    /// <summary>Предустановка разрешения воксельной сетки runtime.</summary>
    public enum VoxelResolutionPreset
    {
        /// <summary>0.01 мм.</summary>
        High = 0,

        /// <summary>0.30 мм.</summary>
        Medium = 1,

        /// <summary>0.60 мм.</summary>
        Coarse = 2,

        /// <summary>Пользовательское значение.</summary>
        Custom = 3
    }

    /// <summary>Разрешение preset в миллиметрах.</summary>
    public static class VoxelResolutionPresetExtensions
    {
        /// <summary>Возвращает шаг сетки (мм) для preset.</summary>
        public static double ToResolutionMm(this VoxelResolutionPreset preset) =>
            preset switch
            {
                VoxelResolutionPreset.High => ProjectConstants.RES_HIGH,
                VoxelResolutionPreset.Medium => ProjectConstants.RES_MEDIUM,
                VoxelResolutionPreset.Coarse => ProjectConstants.RES_COARSE,
                _ => ProjectConstants.RES_MEDIUM
            };

        /// <summary>Отображаемое имя preset.</summary>
        public static string ToDisplayName(this VoxelResolutionPreset preset) =>
            preset switch
            {
                VoxelResolutionPreset.High => "Высокая",
                VoxelResolutionPreset.Medium => "Средняя",
                VoxelResolutionPreset.Coarse => "Грубое",
                _ => "Пользовательское"
            };
    }
}
