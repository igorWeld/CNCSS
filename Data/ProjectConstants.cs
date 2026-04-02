namespace CNCSS.Data
{
    /// <summary>
    /// Глобальные константы проекта CNCSS.
    /// </summary>
    public static class ProjectConstants
    {
        // Параметры станка по умолчанию
        public const double DEFAULT_HOME_X = 255.0;
        public const double DEFAULT_HOME_Y = 255.0;
        public const double DEFAULT_HOME_Z = 255.0;
        public const double DEFAULT_RAPID_FEED = 5000.0;

        // Параметры воксельной модели
        public const int VOXEL_CHUNK_SIZE = 32;
        public const int VOXEL_DATA_LENGTH = 512; // (32*32*32) / 64

        // Настройки визуализации (разрешение)
        public const double RES_HIGH = 0.1;
        public const double RES_MEDIUM = 0.3;
        public const double RES_COARSE = 0.6;

        // Параметры отрисовки
        public const int TOOL_CYLINDER_DIVISIONS = 20;
        public const double EPSILON = 1e-7;
        public const double SMALL_EPSILON = 1e-9;

        // Настройки анимации
        public const double DEFAULT_SPEED_MULTIPLIER = 2.0;
        public const double DEFAULT_OVERRIDE_PERCENT = 100.0;
    }
}
