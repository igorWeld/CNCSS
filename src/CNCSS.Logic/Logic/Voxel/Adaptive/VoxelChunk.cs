using System.Collections;
using System.Numerics;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.Logic.Voxel.Adaptive;

/// <summary>
/// Базовая единица хранения occupancy для адаптивного воксельного движка.
/// Поддерживает Full/Empty/Bitset/RLE режимы для снижения памяти.
/// </summary>
public sealed class VoxelChunk
{
    public const int Size = 16;
    public const int VoxelCount = Size * Size * Size;
    private const int BitsetWordCount = VoxelCount / 64;

    private ulong[]? _bitset;
    private Run[]? _rle;

    public VoxelChunk(
        Point3D origin,
        double voxelSizeMm,
        int lodLevel,
        bool initiallyOccupied = true)
    {
        Origin = origin;
        VoxelSizeMm = voxelSizeMm;
        LodLevel = lodLevel;
        CompressionKind = initiallyOccupied
            ? VoxelChunkCompressionKind.Full
            : VoxelChunkCompressionKind.Empty;
    }

    public Point3D Origin { get; }

    public double VoxelSizeMm { get; }

    public int LodLevel { get; }

    public bool IsDirty { get; private set; }

    public VoxelChunkCompressionKind CompressionKind { get; private set; }

    public void MarkClean() => IsDirty = false;

    public bool IsOccupied(int x, int y, int z)
    {
        ValidateLocal(x, y, z);
        int idx = ToLinearIndex(x, y, z);

        return CompressionKind switch
        {
            VoxelChunkCompressionKind.Full => true,
            VoxelChunkCompressionKind.Empty => false,
            VoxelChunkCompressionKind.Bitset => ReadBitset(idx),
            VoxelChunkCompressionKind.Rle => ReadRle(idx),
            _ => false
        };
    }

    public void SetVoxel(int x, int y, int z, bool occupied)
    {
        ValidateLocal(x, y, z);
        int idx = ToLinearIndex(x, y, z);

        switch (CompressionKind)
        {
            case VoxelChunkCompressionKind.Full when occupied:
            case VoxelChunkCompressionKind.Empty when !occupied:
                return;
        }

        EnsureBitsetMaterialized();
        WriteBitset(idx, occupied);
        IsDirty = true;
    }

    /// <summary>
    /// Пытается уплотнить storage: Full/Empty/RLE/Bitset.
    /// </summary>
    public void TryCompress()
    {
        EnsureBitsetMaterialized();
        if (_bitset == null)
        {
            return;
        }

        int ones = CountOnes(_bitset);
        if (ones == 0)
        {
            CompressionKind = VoxelChunkCompressionKind.Empty;
            _bitset = null;
            _rle = null;
            return;
        }

        if (ones == VoxelCount)
        {
            CompressionKind = VoxelChunkCompressionKind.Full;
            _bitset = null;
            _rle = null;
            return;
        }

        Run[] runs = BuildRuns(_bitset);
        int rleBytes = runs.Length * sizeof(int) * 2;
        int bitsetBytes = _bitset.Length * sizeof(ulong);
        if (rleBytes < bitsetBytes)
        {
            CompressionKind = VoxelChunkCompressionKind.Rle;
            _rle = runs;
            _bitset = null;
            return;
        }

        CompressionKind = VoxelChunkCompressionKind.Bitset;
        _rle = null;
    }

    /// <summary>
    /// Добавляет в builder только воксели, имеющие хотя бы одну внешнюю грань.
    /// </summary>
    public void GetMesh(MeshBuilder builder)
    {
        ForEachOccupied((x, y, z) =>
        {
            if (!HasAnyExteriorFace(x, y, z))
            {
                return;
            }

            double cx = Origin.X + (x + 0.5) * VoxelSizeMm;
            double cy = Origin.Y + (y + 0.5) * VoxelSizeMm;
            double cz = Origin.Z + (z + 0.5) * VoxelSizeMm;
            builder.AddBox(new Point3D(cx, cy, cz), VoxelSizeMm, VoxelSizeMm, VoxelSizeMm);
        });
    }

    public void ForEachOccupied(Action<int, int, int> action)
    {
        for (int z = 0; z < Size; z++)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    if (IsOccupied(x, y, z))
                    {
                        action(x, y, z);
                    }
                }
            }
        }
    }

    public BitArray ExportBitArray()
    {
        var bits = new BitArray(VoxelCount);
        for (int i = 0; i < VoxelCount; i++)
        {
            bits[i] = IsOccupied(i % Size, (i / Size) % Size, i / (Size * Size));
        }

        return bits;
    }

    public void ImportBitArray(BitArray bits)
    {
        if (bits.Length != VoxelCount)
        {
            throw new ArgumentException($"Expected {VoxelCount} bits.", nameof(bits));
        }

        _bitset = new ulong[BitsetWordCount];
        for (int i = 0; i < VoxelCount; i++)
        {
            WriteBitset(i, bits[i]);
        }

        CompressionKind = VoxelChunkCompressionKind.Bitset;
        _rle = null;
        TryCompress();
        IsDirty = true;
    }

    private bool HasAnyExteriorFace(int x, int y, int z)
    {
        ReadOnlySpan<(int dx, int dy, int dz)> dirs = stackalloc (int, int, int)[]
        {
            (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)
        };

        foreach ((int dx, int dy, int dz) in dirs)
        {
            int nx = x + dx;
            int ny = y + dy;
            int nz = z + dz;
            if (!IsInside(nx, ny, nz) || !IsOccupied(nx, ny, nz))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInside(int x, int y, int z) =>
        x >= 0 && x < Size && y >= 0 && y < Size && z >= 0 && z < Size;

    private void EnsureBitsetMaterialized()
    {
        if (_bitset != null)
        {
            return;
        }

        _bitset = new ulong[BitsetWordCount];
        switch (CompressionKind)
        {
            case VoxelChunkCompressionKind.Full:
                for (int i = 0; i < _bitset.Length; i++)
                {
                    _bitset[i] = ulong.MaxValue;
                }

                break;
            case VoxelChunkCompressionKind.Empty:
                break;
            case VoxelChunkCompressionKind.Rle:
                MaterializeRleToBitset();
                break;
            case VoxelChunkCompressionKind.Bitset:
                break;
        }

        CompressionKind = VoxelChunkCompressionKind.Bitset;
        _rle = null;
    }

    private void MaterializeRleToBitset()
    {
        if (_rle == null || _bitset == null)
        {
            return;
        }

        int idx = 0;
        foreach (Run run in _rle)
        {
            if (run.Occupied)
            {
                for (int i = 0; i < run.Length; i++)
                {
                    WriteBitset(idx + i, true);
                }
            }

            idx += run.Length;
            if (idx >= VoxelCount)
            {
                break;
            }
        }
    }

    private bool ReadBitset(int idx)
    {
        if (_bitset == null)
        {
            return false;
        }

        int word = idx >> 6;
        int bit = idx & 63;
        return (_bitset[word] & (1UL << bit)) != 0;
    }

    private void WriteBitset(int idx, bool occupied)
    {
        if (_bitset == null)
        {
            return;
        }

        int word = idx >> 6;
        int bit = idx & 63;
        ulong mask = 1UL << bit;
        if (occupied)
        {
            _bitset[word] |= mask;
        }
        else
        {
            _bitset[word] &= ~mask;
        }
    }

    private bool ReadRle(int idx)
    {
        if (_rle == null)
        {
            return false;
        }

        int cursor = 0;
        foreach (Run run in _rle)
        {
            int end = cursor + run.Length;
            if (idx < end)
            {
                return run.Occupied;
            }

            cursor = end;
        }

        return false;
    }

    private static Run[] BuildRuns(ulong[] bits)
    {
        var runs = new List<Run>(64);
        bool current = GetBit(bits, 0);
        int len = 1;

        for (int i = 1; i < VoxelCount; i++)
        {
            bool value = GetBit(bits, i);
            if (value == current)
            {
                len++;
                continue;
            }

            runs.Add(new Run(current, len));
            current = value;
            len = 1;
        }

        runs.Add(new Run(current, len));
        return runs.ToArray();
    }

    private static bool GetBit(ulong[] bits, int idx)
    {
        int word = idx >> 6;
        int bit = idx & 63;
        return (bits[word] & (1UL << bit)) != 0;
    }

    private static int CountOnes(ulong[] bits)
    {
        int count = 0;
        foreach (ulong w in bits)
        {
            count += BitOperations.PopCount(w);
        }

        return count;
    }

    private static int ToLinearIndex(int x, int y, int z) =>
        x + Size * (y + Size * z);

    private static void ValidateLocal(int x, int y, int z)
    {
        if (!IsInside(x, y, z))
        {
            throw new ArgumentOutOfRangeException($"Voxel index out of range: ({x},{y},{z})");
        }
    }

    private readonly record struct Run(bool Occupied, int Length);
}

public enum VoxelChunkCompressionKind
{
    Empty = 0,
    Full = 1,
    Bitset = 2,
    Rle = 3
}
