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

        public void SetVoxel(int x, int y, int z, bool value)
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
            }
            else
            {
                _data[ulongIdx] &= ~bit;
                _filledVoxelCount--;
            }

            IsDirty = true;
            IsEmpty = _filledVoxelCount <= 0;
            IsFull = _filledVoxelCount >= VoxelCount;
        }

        public ulong[] GetData() => _data;
    }
}
