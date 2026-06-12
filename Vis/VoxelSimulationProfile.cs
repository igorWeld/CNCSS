using CNCSS.Data;

namespace CNCSS.Vis
{
    /// <summary>Параметры бюджета воксельной симуляции (шаг съёма, лимиты меша) в зависимости от выбранного разрешения сетки.</summary>
    public readonly record struct VoxelSimulationProfile(
        string Name,
        double ResolutionMm,
        double CutStepMm,
        double MaxLeadMm,
        int MeshBudgetMs,
        int DirtyChunkBudget)
    {
        /// <summary>Создаёт профиль. <see cref="DirtyChunkBudget"/> — максимум чанков 32³, пересобираемых за один кадр обновления меша (снижает фризы UI).</summary>
        public static VoxelSimulationProfile ForResolution(double resolutionMm)
        {
            if (resolutionMm <= ProjectConstants.RES_HIGH + 1e-9)
            {
                return new VoxelSimulationProfile(
                    Name: "high",
                    ResolutionMm: ProjectConstants.RES_HIGH,
                    CutStepMm: 0.08,
                    MaxLeadMm: 0.00,
                    MeshBudgetMs: 12,
                    DirtyChunkBudget: 10);
            }

            if (resolutionMm <= ProjectConstants.RES_MEDIUM + 1e-9)
            {
                return new VoxelSimulationProfile(
                    Name: "medium",
                    ResolutionMm: ProjectConstants.RES_MEDIUM,
                    CutStepMm: 0.35,
                    MaxLeadMm: 0.20,
                    MeshBudgetMs: 12,
                    DirtyChunkBudget: 20);
            }

            if (resolutionMm <= ProjectConstants.RES_COARSE + 1e-9)
            {
                return new VoxelSimulationProfile(
                    Name: "coarse",
                    ResolutionMm: ProjectConstants.RES_COARSE,
                    CutStepMm: 0.58,
                    MaxLeadMm: 0.60,
                    MeshBudgetMs: 14,
                    DirtyChunkBudget: 28);
            }

            double step = Math.Clamp(resolutionMm * 1.2, 0.05, 1.0);
            return new VoxelSimulationProfile(
                Name: $"custom-{resolutionMm:F3}",
                ResolutionMm: resolutionMm,
                CutStepMm: step,
                MaxLeadMm: step,
                MeshBudgetMs: 12,
                DirtyChunkBudget: 24);
        }
    }
}
