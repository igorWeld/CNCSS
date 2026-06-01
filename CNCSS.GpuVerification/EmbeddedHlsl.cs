namespace CNCSS.GpuVerification;

internal static class EmbeddedHlsl
{
    public const string CsSource =
        """
        RWStructuredBuffer<uint> Occupancy;

        cbuffer InitCBuffer : register(b0)
        {
            uint Ini_DimX;
            uint Ini_DimY;
            uint Ini_DimZ;
            uint Ini_TotalCells;
        };

        [numthreads(256, 1, 1)]
        void CSInit(uint3 DTid : SV_DispatchThreadID)
        {
            uint lid = DTid.x;
            if (lid >= Ini_TotalCells)
                return;
            Occupancy[lid] = 1u;
        }

        cbuffer CutCBuffer : register(b0)
        {
            float4 Cut_CornerMinVoxel;
            uint4 Cut_BoxA;
            uint4 Cut_BoxB;
            uint4 Cut_DimsPad;
            float4 Cut_StartRadSq;
            float4 Cut_DirLen2XY;
            float4 Cut_FlutePad;
        };

        uint FlatIdx(uint ix, uint iy, uint iz, uint dx, uint dy)
        {
            return ix + dx * (iy + dy * iz);
        }

        [numthreads(256, 1, 1)]
        void CSCut(uint3 DTid : SV_DispatchThreadID)
        {
            uint rangeX = Cut_BoxA.w;
            uint rangeY = Cut_BoxB.x;
            uint rangeZ = Cut_BoxB.y;
            uint boxVol = rangeX * rangeY * rangeZ;
            uint tid = DTid.x;
            if (tid >= boxVol)
                return;

            uint t2 = tid;
            uint lx = t2 % rangeX;
            t2 /= rangeX;
            uint ly = t2 % rangeY;
            uint lz = t2 / rangeY;

            uint ix = Cut_BoxA.x + lx;
            uint iy = Cut_BoxA.y + ly;
            uint iz = Cut_BoxA.z + lz;

            uint dimX = Cut_BoxB.z;
            uint dimY = Cut_BoxB.w;
            uint dimZ = Cut_DimsPad.x;

            if (ix >= dimX || iy >= dimY || iz >= dimZ)
                return;

            float voxel = Cut_CornerMinVoxel.w;
            float px = Cut_CornerMinVoxel.x + (float)ix * voxel;
            float py = Cut_CornerMinVoxel.y + (float)iy * voxel;

            float len2 = Cut_DirLen2XY.w;
            float t;
            if (len2 < 1e-7f)
                t = 0.0f;
            else
            {
                float3 s = Cut_StartRadSq.xyz;
                float3 d = Cut_DirLen2XY.xyz;
                t = ((px - s.x) * d.x + (py - s.y) * d.y) / len2;
                t = t < 0.0f ? 0.0f : (t > 1.0f ? 1.0f : t);
            }

            float sx = Cut_StartRadSq.x;
            float sy = Cut_StartRadSq.y;
            float dxw = Cut_DirLen2XY.x;
            float dyw = Cut_DirLen2XY.y;

            float qx = sx + t * dxw - px;
            float qy = sy + t * dyw - py;
            if (qx * qx + qy * qy > Cut_StartRadSq.w + 1e-10f)
                return;

            float tipZ = Cut_StartRadSq.z + t * Cut_DirLen2XY.z;
            float topZ = tipZ + Cut_FlutePad.x;
            float pz = Cut_CornerMinVoxel.z + (float)iz * voxel;
            float nextZ = pz + voxel;

            if (topZ <= pz || tipZ >= nextZ)
                return;

            uint idx = FlatIdx(ix, iy, iz, dimX, dimY);
            Occupancy[idx] = 0u;
        }
        """;

    internal const int ThreadsPerGroup = 256;
}
