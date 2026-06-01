namespace CNCSS.Data.Tools
{
    /// <summary>
    /// Чанк вокселей размером 32x32x32.
    /// Использует битовую маску для хранения состояния (1 - есть материал, 0 - вырезано).
    /// </summary>
    public class VoxelChunk
    {
        public const int Size = ProjectConstants.VOXEL_CHUNK_SIZE;
        private const int VoxelCount = Size * Size * Size;
        private const int SparseToDenseThreshold = 4096;
        private ulong[]? _data;
        private HashSet<int>? _removedSparse;
        private Dictionary<int, uint>? _removedVoxelColors;
        private int _filledVoxelCount;

        public bool IsDirty { get; set; } = true;
        public bool IsEmpty { get; private set; } = false;
        public bool IsFull { get; private set; } = true;

        public VoxelChunk(bool fill = true)
        {
            if (fill)
            {
                // Полностью цельный чанк не хранит 32^3 бит до первого реального реза.
                _data = null;
                _filledVoxelCount = VoxelCount;
                IsFull = true;
                IsEmpty = false;
            }
            else
            {
                _data = null;
                _filledVoxelCount = 0;
                IsFull = false;
                IsEmpty = true;
            }
        }

        public bool GetVoxel(int x, int y, int z)
        {
            int index = (x * Size + y) * Size + z;
            if (_data != null)
            {
                return (_data[index >> 6] & (1UL << (index & 63))) != 0;
            }

            if (_removedSparse != null)
            {
                // Sparse mode: base is "full", sparse set stores removed voxels only.
                return !_removedSparse.Contains(index);
            }

            if (IsFull)
            {
                return true;
            }

            if (IsEmpty)
            {
                return false;
            }

            // Fallback for safety: if state is partial and dense buffer is absent.
            ulong[] data = EnsureDataMaterialized(fill: false);
            return (data[index >> 6] & (1UL << (index & 63))) != 0;
        }

        public void SetVoxel(int x, int y, int z, bool value, uint removedByToolColor = 0)
        {
            int index = (x * Size + y) * Size + z;
            bool wasSet = GetVoxel(x, y, z);
            if (wasSet == value)
            {
                return;
            }

            if (value)
            {
                if (_removedSparse != null)
                {
                    _removedSparse.Remove(index);
                }
                else
                {
                    int ulongIdx = index >> 6;
                    ulong bit = 1UL << (index & 63);
                    ulong[] data = EnsureDataMaterialized(fill: IsFull);
                    data[ulongIdx] |= bit;
                }

                _filledVoxelCount++;
                _removedVoxelColors?.Remove(index);
            }
            else
            {
                if (_data == null && _removedSparse == null && IsFull)
                {
                    _removedSparse = new HashSet<int>();
                }

                if (_removedSparse != null)
                {
                    _removedSparse.Add(index);
                    EnsureDenseIfSparseTooLarge();
                }
                else
                {
                    int ulongIdx = index >> 6;
                    ulong bit = 1UL << (index & 63);
                    ulong[] data = EnsureDataMaterialized(fill: IsFull);
                    data[ulongIdx] &= ~bit;
                }

                _filledVoxelCount--;
                if (removedByToolColor != 0)
                {
                    _removedVoxelColors ??= new Dictionary<int, uint>();
                    _removedVoxelColors[index] = removedByToolColor;
                }
            }

            IsDirty = true;
            IsEmpty = _filledVoxelCount <= 0;
            IsFull = _filledVoxelCount >= VoxelCount;
            CompactIfUniform();
        }

        public bool ClearVoxel(int x, int y, int z, uint removedByToolColor = 0)
        {
            int index = (x * Size + y) * Size + z;
            if (!GetVoxel(x, y, z))
            {
                return false;
            }

            if (_data == null && _removedSparse == null && IsFull)
            {
                _removedSparse = new HashSet<int>();
            }

            if (_removedSparse != null)
            {
                _removedSparse.Add(index);
                EnsureDenseIfSparseTooLarge();
            }
            else
            {
                int ulongIdx = index >> 6;
                ulong bit = 1UL << (index & 63);
                ulong[] data = EnsureDataMaterialized(fill: IsFull);
                data[ulongIdx] &= ~bit;
            }

            _filledVoxelCount--;
            if (removedByToolColor != 0)
            {
                _removedVoxelColors ??= new Dictionary<int, uint>();
                _removedVoxelColors[index] = removedByToolColor;
            }

            IsDirty = true;
            IsEmpty = _filledVoxelCount <= 0;
            IsFull = _filledVoxelCount >= VoxelCount;
            CompactIfUniform();
            return true;
        }

        public bool TryGetRemovedVoxelColor(int x, int y, int z, out uint color)
        {
            color = 0;
            if (_removedVoxelColors == null)
            {
                return false;
            }

            int index = (x * Size + y) * Size + z;
            return _removedVoxelColors.TryGetValue(index, out color);
        }

        public ulong[] GetData()
        {
            if (_data != null)
            {
                return _data;
            }

            if (_removedSparse != null)
            {
                _data = EnsureDataMaterialized(fill: true);
                foreach (int index in _removedSparse)
                {
                    int ulongIdx = index >> 6;
                    ulong bit = 1UL << (index & 63);
                    _data[ulongIdx] &= ~bit;
                }

                _removedSparse = null;
                return _data;
            }

            return EnsureDataMaterialized(fill: IsFull);
        }

        private ulong[] EnsureDataMaterialized(bool fill)
        {
            if (_data != null)
            {
                return _data;
            }

            _data = new ulong[ProjectConstants.VOXEL_DATA_LENGTH];
            if (fill)
            {
                for (int i = 0; i < _data.Length; i++)
                {
                    _data[i] = ulong.MaxValue;
                }
            }

            return _data;
        }

        private void CompactIfUniform()
        {
            if (IsFull)
            {
                _data = null;
                _removedSparse = null;
                _removedVoxelColors?.Clear();
            }
            else if (IsEmpty)
            {
                _data = null;
                _removedSparse = null;
            }
        }

        private void EnsureDenseIfSparseTooLarge()
        {
            if (_removedSparse == null || _removedSparse.Count <= SparseToDenseThreshold)
            {
                return;
            }

            _data = EnsureDataMaterialized(fill: true);
            foreach (int index in _removedSparse)
            {
                int ulongIdx = index >> 6;
                ulong bit = 1UL << (index & 63);
                _data[ulongIdx] &= ~bit;
            }

            _removedSparse = null;
        }
    }
}
