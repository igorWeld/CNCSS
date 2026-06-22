namespace CNCSS.Data.Config
{
    /// <summary>Константы цикла симуляции и playback.</summary>
    public static class SimulationConstants
    {
        /// <summary>Минимальный коэффициент замедления FPS.</summary>
        public const double FpsSlowdownFloor = 0.1;

        /// <summary>Порог количества резов для параллельной конвертации в GPU batch.</summary>
        public const int ParallelCutConvertThreshold = 2048;

        /// <summary>Максимальное отставание инструмента от contact mesh (мм) перед hold.</summary>
        public const double MaxToolLeadMm = 0.5;

        /// <summary>Contact margin при адаптивной зоне (доля радиуса).</summary>
        public const double AdaptiveContactMarginRadiusFactor = 0.25;

        /// <summary>Невидимый цилиндр приближения: радиус режущего инструмента × 1.56 (+30% к 1.2).</summary>
        public const double AdaptiveProximityRadiusFactor = 1.56;

        /// <summary>Множитель разрешения для trigger distance air-cut.</summary>
        public const double AirCutTriggerResolutionFactor = 4.0;

        /// <summary>Множитель радиуса для trigger distance air-cut.</summary>
        public const double AirCutTriggerRadiusFactor = 0.35;
    }
}
