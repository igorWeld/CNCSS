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
        private readonly ulong[] _data = new ulong[ProjectConstants.VOXEL_DATA_LENGTH];
        private Dictionary<int, uint>? _removedVoxelColors;
        private int _filledVoxelCount;

        public bool IsDirty { get; set; } = true;
        public bool IsEmpty { get; private set; } = false;
        public bool IsFull { get; private set; } = true;

        public VoxelChunk(bool fill = true)
        {
            if (fill)
            {
                for (int i = 0; i < _data.Length; i++) _data[i] = ulong.MaxValue;
                _filledVoxelCount = VoxelCount;
                IsFull = true;
                IsEmpty = false;
            }
            else
            {
                for (int i = 0; i < _data.Length; i++) _data[i] = 0;
                _filledVoxelCount = 0;
                IsFull = false;
                IsEmpty = true;
            }
        }

        public bool GetVoxel(int x, int y, int z)
        {
            int index = (x * Size + y) * Size + z;
            return (_data[index >> 6] & (1UL << (index & 63))) != 0;
        }

        public void SetVoxel(int x, int y, int z, bool value, uint removedByToolColor = 0)
        {
            int index = (x * Size + y) * Size + z;
            int ulongIdx = index >> 6;
            ulong bit = 1UL << (index & 63);

            bool wasSet = (_data[ulongIdx] & bit) != 0;
            if (wasSet == value)
                return;

            if (value)
            {
                _data[ulongIdx] |= bit;
                _filledVoxelCount++;
                _removedVoxelColors?.Remove(index);
            }
            else
            {
                _data[ulongIdx] &= ~bit;
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

        public ulong[] GetData() => _data;
    }
}
