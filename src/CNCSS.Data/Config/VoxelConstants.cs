namespace CNCSS.Data.Config
{
    using System.Threading.Tasks;

    /// <summary>Константы воксельной симуляции (playback, adaptive voxel).</summary>
    public static class VoxelConstants
    {
        /// <summary>Разрешение вокселей runtime (мм).</summary>
        public const double VoxelResolutionMm = 0.10;

        /// <summary>Целевой FPS визуализации playback.</summary>
        public const float PlaybackTargetFps = 35f;

        /// <summary>Минимально допустимый FPS обработки.</summary>
        public const float PlaybackMinFps = 35f;

        /// <summary>Бюджет кадра cut+render (мс) для 35 FPS.</summary>
        public const double PlaybackFrameBudgetMs = 1000.0 / PlaybackTargetFps;

        /// <summary>Минимальный коэффициент playback при отставании cut+render.</summary>
        public const double PlaybackMeshBackpressureFloor = 0.55;

        /// <summary>Минимальное перемещение TCP для реза (мм).</summary>
        public const double CutMotionEpsilonMm = 0.01;

        /// <summary>Параллельная инициализация: все доступные ядра.</summary>
        public static readonly ParallelOptions InitParallelOptions = new() { MaxDegreeOfParallelism = -1 };

        /// <summary>Подвод инструмента: старт расчёта вокселей при расстоянии до заготовки (мм).</summary>
        public const double VoxelWarmupApproachDistanceMm = 10.0;

        /// <summary>Минимальный целевой FPS viewport (после загрузки заготовки).</summary>
        public const float ViewportMinFps = 35f;

        /// <summary>Look-ahead кадров траектории для prefetch чанков.</summary>
        public const int CutLookAheadFrames = 4;

        /// <summary>Макс. частота обновления mesh заготовки на экране (Гц).</summary>
        public const float VisualUpdateMaxHz = 15f;

        /// <summary>Макс. dirty-чанков за один visual tick.</summary>
        public const int MaxDirtyChunksPerVisualTick = 48;

        /// <summary>Размер батча фонового прогрева display-mesh.</summary>
        public const int DisplayWarmupBatchSize = 64;

        /// <summary>Сетка адаптивной визуализации (грубо), мм.</summary>
        public const double DisplayGridCoarseMm = 0.50;

        /// <summary>Сетка адаптивной визуализации (средне), мм.</summary>
        public const double DisplayGridMediumMm = 0.30;

        /// <summary>Сетка адаптивной визуализации (точно), мм.</summary>
        public const double DisplayGridFineMm = 0.10;

        /// <summary>Сетка адаптивной визуализации по умолчанию.</summary>
        public const double DisplayGridDefaultMm = DisplayGridMediumMm;

        public static int ComputeDisplayLodStride(double simulationResolutionMm) =>
            ComputeDisplayLodStride(simulationResolutionMm, DisplayGridDefaultMm);

        public static int ComputeDisplayLodStride(double simulationResolutionMm, double displayGridMm)
        {
            if (simulationResolutionMm <= 1e-9)
            {
                return 1;
            }

            double target = NormalizeDisplayGridMm(displayGridMm);
            return Math.Max(1, (int)Math.Ceiling(target / simulationResolutionMm));
        }

        public static double NormalizeDisplayGridMm(double valueMm)
        {
            if (Math.Abs(valueMm - DisplayGridFineMm) < 1e-9)
            {
                return DisplayGridFineMm;
            }

            if (Math.Abs(valueMm - DisplayGridMediumMm) < 1e-9)
            {
                return DisplayGridMediumMm;
            }

            if (Math.Abs(valueMm - DisplayGridCoarseMm) < 1e-9)
            {
                return DisplayGridCoarseMm;
            }

            // Приведение пользовательского значения к ближайшему preset.
            double[] presets = new[] { DisplayGridCoarseMm, DisplayGridMediumMm, DisplayGridFineMm };
            double best = presets[0];
            double bestDist = Math.Abs(valueMm - best);
            for (int i = 1; i < presets.Length; i++)
            {
                double dist = Math.Abs(valueMm - presets[i]);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = presets[i];
                }
            }

            return best;
        }

        public static string GetDisplayGridLabel(double valueMm)
        {
            double normalized = NormalizeDisplayGridMm(valueMm);
            if (Math.Abs(normalized - DisplayGridFineMm) < 1e-9)
            {
                return "точно";
            }

            if (Math.Abs(normalized - DisplayGridMediumMm) < 1e-9)
            {
                return "средне";
            }

            return "грубо";
        }
    }
}
