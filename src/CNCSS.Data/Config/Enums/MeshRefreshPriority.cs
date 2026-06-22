namespace CNCSS.Data.Config.Enums
{
    /// <summary>Приоритет пересборки mesh (MeshScheduler).</summary>
    public enum MeshRefreshPriority
    {
        /// <summary>Contact L3 — наивысший.</summary>
        ContactL3 = 0,

        /// <summary>Contact L2/L1.</summary>
        ContactL2L1 = 1,

        /// <summary>Видимые камерой.</summary>
        CameraVisible = 2,

        /// <summary>Остальные зоны.</summary>
        Rest = 3
    }
}
