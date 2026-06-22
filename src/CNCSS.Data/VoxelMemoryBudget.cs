namespace CNCSS.Data;

/// <summary>Оценка памяти воксельной заготовки по габаритам (для будущего C#-движка).</summary>
public static class VoxelMemoryBudget
{
    private const double ChunkWorldMm = 1.6;
    private const ulong BytesPerChunk = 4120;
    private const ulong MinBudgetBytes = 32ul * 1024 * 1024;
    private const ulong MaxBudgetBytes = 256ul * 1024 * 1024;
    private const ulong OverheadBytes = 8ul * 1024 * 1024;

    public static ulong ComputeBudgetBytes(StockVolumeConfig volume)
    {
        double sizeX = Math.Max(0.001, volume.MaxX - volume.MinX);
        double sizeY = Math.Max(0.001, volume.MaxY - volume.MinY);
        double sizeZ = Math.Max(0.001, volume.MaxZ - volume.MinZ);

        int cx1 = Math.Max(0, (int)Math.Floor((sizeX - 1e-6) / ChunkWorldMm));
        int cy1 = Math.Max(0, (int)Math.Floor((sizeY - 1e-6) / ChunkWorldMm));
        int cz1 = Math.Max(0, (int)Math.Floor((sizeZ - 1e-6) / ChunkWorldMm));

        long shellChunks = 2L * (
            (long)(cx1 + 1) * (cy1 + 1) +
            (long)(cx1 + 1) * (cz1 + 1) +
            (long)(cy1 + 1) * (cz1 + 1));
        shellChunks = Math.Max(shellChunks, 32);

        // Оболочка + запас на interior-чанки при резе.
        ulong estimated = (ulong)shellChunks * BytesPerChunk * 2ul + OverheadBytes;
        if (estimated < MinBudgetBytes)
        {
            return MinBudgetBytes;
        }

        if (estimated > MaxBudgetBytes)
        {
            return MaxBudgetBytes;
        }

        return estimated;
    }
}
