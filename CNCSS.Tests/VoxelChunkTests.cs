using System.Collections;
using System.Windows.Media.Media3D;
using CNCSS.Logic.Voxel.Adaptive;
using Xunit;

namespace CNCSS.Tests;

public sealed class VoxelChunkTests
{
    [Fact]
    public void Full_chunk_reports_occupied_voxels()
    {
        var chunk = new VoxelChunk(new Point3D(0, 0, 0), 0.1, 0, initiallyOccupied: true);
        Assert.True(chunk.IsOccupied(0, 0, 0));
        Assert.True(chunk.IsOccupied(15, 15, 15));
        Assert.Equal(VoxelChunkCompressionKind.Full, chunk.CompressionKind);
    }

    [Fact]
    public void SetVoxel_then_TryCompress_transitions_to_empty()
    {
        var chunk = new VoxelChunk(new Point3D(0, 0, 0), 0.1, 0, initiallyOccupied: true);
        for (int z = 0; z < VoxelChunk.Size; z++)
        {
            for (int y = 0; y < VoxelChunk.Size; y++)
            {
                for (int x = 0; x < VoxelChunk.Size; x++)
                {
                    chunk.SetVoxel(x, y, z, false);
                }
            }
        }

        chunk.TryCompress();
        Assert.Equal(VoxelChunkCompressionKind.Empty, chunk.CompressionKind);
    }

    [Fact]
    public void Export_import_bitarray_preserves_voxels()
    {
        var src = new VoxelChunk(new Point3D(0, 0, 0), 0.1, 0, initiallyOccupied: false);
        src.SetVoxel(1, 2, 3, true);
        src.SetVoxel(10, 11, 12, true);
        src.TryCompress();

        BitArray bits = src.ExportBitArray();
        var dst = new VoxelChunk(new Point3D(0, 0, 0), 0.1, 0, initiallyOccupied: true);
        dst.ImportBitArray(bits);

        Assert.True(dst.IsOccupied(1, 2, 3));
        Assert.True(dst.IsOccupied(10, 11, 12));
        Assert.False(dst.IsOccupied(0, 0, 0));
    }
}
