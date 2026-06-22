namespace CNCSS.Data;

/// <summary>Снимок статистики воксельной заготовки для HUD.</summary>
public sealed record StockVoxelDisplayStats(
    double SizeXmm,
    double SizeYmm,
    double SizeZmm,
    int SurfaceVoxelCount,
    int ChunkCount,
    int DirtyChunkCount,
    double VoxelResolutionMm,
    bool ShellDisplayed);
