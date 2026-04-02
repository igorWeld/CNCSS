using System.Collections;

namespace CNCSS.Data.Tools
{
    /// <summary>
    /// Чанк вокселей размером 16x16x16.
    /// Использует битовую маску для хранения состояния (1 - есть материал, 0 - вырезано).
    /// </summary>
    public class VoxelChunk
    {
        public const int Size = 32;
        private readonly ulong[] _data = new ulong[512]; // 32*32*32 / 64 = 512 ulongs

        public bool IsDirty { get; set; } = true;
        public bool IsEmpty { get; private set; } = false;
        public bool IsFull { get; private set; } = true;

        public VoxelChunk(bool fill = true)
        {
            if (fill)
            {
                for (int i = 0; i < _data.Length; i++) _data[i] = ulong.MaxValue;
                IsFull = true;
                IsEmpty = false;
            }
            else
            {
                IsFull = false;
                IsEmpty = true;
            }
        }

        public bool GetVoxel(int x, int y, int z)
        {
            // 32x32x32: x << 10 | y << 5 | z
            int index = (x << 10) | (y << 5) | z;
            return (_data[index >> 6] & (1UL << (index & 63))) != 0;
        }

        public void SetVoxel(int x, int y, int z, bool value)
        {
            int index = (x << 10) | (y << 5) | z;
            int ulongIdx = index >> 6;
            ulong bit = 1UL << (index & 63);

            if (value) _data[ulongIdx] |= bit;
            else _data[ulongIdx] &= ~bit;
            
            IsDirty = true;
            UpdateFlags();
        }

        private void UpdateFlags()
        {
            bool any = false;
            bool all = true;
            for (int i = 0; i < _data.Length; i++)
            {
                if (_data[i] != 0) any = true;
                if (_data[i] != ulong.MaxValue) all = false;
            }
            IsEmpty = !any;
            IsFull = all;
        }

        public ulong[] GetData() => _data;
    }
}
