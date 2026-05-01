using CNCSS.Data;

namespace CNCSS.Vis
{
    /// <summary>Параметры бюджета воксельной симуляции (шаг съёма, лимиты меша) в зависимости от выбранного разрешения сетки.</summary>
    public readonly record struct VoxelSimulationProfile(
        double ResolutionMm,
        double CutStepMm,
        double MaxLeadMm,
        int MeshBudgetMs,
        int DirtyChunkBudget)
    {
        public static VoxelSimulationProfile ForResolution(double resolutionMm)
        {
            if (resolutionMm <= ProjectConstants.RES_HIGH + 1e-9)
            {
                return new VoxelSimulationProfile(
                    ResolutionMm: ProjectConstants.RES_HIGH,
                    CutStepMm: 0.05,
                    MaxLeadMm: 0.00,
                    MeshBudgetMs: 10,
                    DirtyChunkBudget: 1200);
            }

            if (resolutionMm <= ProjectConstants.RES_MEDIUM + 1e-9)
            {
                return new VoxelSimulationProfile(
                    ResolutionMm: ProjectConstants.RES_MEDIUM,
                    CutStepMm: 0.20,
                    MaxLeadMm: 0.20,
                    MeshBudgetMs: 12,
                    DirtyChunkBudget: 1600);
            }

            return new VoxelSimulationProfile(
                ResolutionMm: ProjectConstants.RES_COARSE,
                CutStepMm: 0.40,
                MaxLeadMm: 0.60,
                MeshBudgetMs: 14,
                DirtyChunkBudget: 2000);
        }
    }
}
