using System.IO;
using System.Text.Json;

namespace CNCSS.Logic.Voxel.Adaptive;

/// <summary>Сохранение/загрузка состояния адаптивной воксельной заготовки.</summary>
public sealed class StockSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public async Task SaveAsync(string filePath, AdaptiveVoxelStockState state, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        await using FileStream fs = File.Create(filePath);
        await JsonSerializer.SerializeAsync(fs, state, JsonOptions, ct);
    }

    public async Task<AdaptiveVoxelStockState> LoadAsync(string filePath, CancellationToken ct = default)
    {
        await using FileStream fs = File.OpenRead(filePath);
        AdaptiveVoxelStockState? state = await JsonSerializer.DeserializeAsync<AdaptiveVoxelStockState>(fs, JsonOptions, ct);
        if (state == null)
        {
            throw new InvalidOperationException("Invalid stock snapshot.");
        }

        return state;
    }
}
