using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using DxgiFormat = Vortice.DXGI.Format;

namespace CNCSS.GpuVerification;

internal sealed class D3d11OccupancyGpuContext : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11ComputeShader _csInit;
    private readonly ID3D11ComputeShader _csCut;
    private readonly ID3D11Buffer _occupancyBuffer;
    private readonly ID3D11UnorderedAccessView _occupancyUav;
    private readonly ID3D11Buffer _stagingReadback;
    private readonly ID3D11Buffer _constantBufferDynamic;
    private readonly int _cellCount;
    private readonly int _dimX;
    private readonly int _dimY;
    private readonly int _dimZ;

    public int DimX => _dimX;

    public int DimY => _dimY;

    public int DimZ => _dimZ;

    public int CellCount => _cellCount;

    public D3d11OccupancyGpuContext(int dimX, int dimY, int dimZ)
    {
        if (dimX <= 0 || dimY <= 0 || dimZ <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimX));
        }

        _dimX = dimX;
        _dimY = dimY;
        _dimZ = dimZ;
        checked
        {
            _cellCount = dimX * dimY * dimZ;
        }

        D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_0],
                out _device!,
                out _,
                out _context!)
            .CheckError();

        ReadOnlyMemory<byte> initBc = Compiler.Compile(EmbeddedHlsl.CsSource, "CSInit", "gpu_stock.hlsl", "cs_5_0");
        ReadOnlyMemory<byte> cutBc = Compiler.Compile(EmbeddedHlsl.CsSource, "CSCut", "gpu_stock.hlsl", "cs_5_0");
        _csInit = _device.CreateComputeShader(initBc.Span);
        _csCut = _device.CreateComputeShader(cutBc.Span);

        int byteWidth = _cellCount * sizeof(uint);
        _occupancyBuffer = _device.CreateBuffer(
            new BufferDescription(
                (uint)byteWidth,
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                structureByteStride: (uint)sizeof(uint)));

        _occupancyUav = _device.CreateUnorderedAccessView(
            _occupancyBuffer,
            new UnorderedAccessViewDescription(_occupancyBuffer, DxgiFormat.Unknown, firstElement: 0, numElements: (uint)_cellCount));

        _stagingReadback = _device.CreateBuffer(
            new BufferDescription(
                (uint)byteWidth,
                BindFlags.None,
                ResourceUsage.Staging,
                CpuAccessFlags.Read));

        _constantBufferDynamic = _device.CreateBuffer(
            new BufferDescription(256, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
    }

    public static GpuVerificationProbeResult ProbeHardware()
    {
        try
        {
            using var ctx = new D3d11OccupancyGpuContext(4, 4, 4);
            ctx.InitializeOccupancy();
            (ulong dedicatedBytes, ulong sharedBytes) = TryGetGpuMemoryBytes(ctx._device);
            return new GpuVerificationProbeResult(true, "Direct3D 11 compute готов", dedicatedBytes, sharedBytes);
        }
        catch (Exception ex)
        {
            return new GpuVerificationProbeResult(false, ex.Message);
        }
    }

    private static (ulong DedicatedBytes, ulong SharedBytes) TryGetGpuMemoryBytes(ID3D11Device device)
    {
        try
        {
            using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
            AdapterDescription desc = adapter.Description;
            return (desc.DedicatedVideoMemory, desc.SharedSystemMemory);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>Все ячейки = 1 (материал). Вызывается один раз перед инкрементальными резами или полным офлайном.</summary>
    public void InitializeOccupancy()
    {
        _context.CSSetUnorderedAccessViews(0u, new[] { _occupancyUav });
        var init = new InitConstants
        {
            DimX = (uint)_dimX,
            DimY = (uint)_dimY,
            DimZ = (uint)_dimZ,
            TotalCells = (uint)_cellCount
        };
        WriteConstantBuffer(init);
        _context.CSSetConstantBuffers(0u, new[] { _constantBufferDynamic });
        _context.CSSetShader(_csInit);
        int initGroups = (_cellCount + EmbeddedHlsl.ThreadsPerGroup - 1) / EmbeddedHlsl.ThreadsPerGroup;
        _context.Dispatch((uint)initGroups, 1, 1);
    }

    /// <summary>Вычесть один цилиндрический сегмент из текущего буфера (без readback).</summary>
    public void ApplyCut(GpuCylinderCut cut, GpuStockBounds bounds, double resolutionMm)
    {
        ApplyCutInternal(cut, bounds.MinX, bounds.MinY, bounds.MinZ, resolutionMm);
    }

    public uint[] ReadOccupancyToCpu()
    {
        _context.CopyResource(_stagingReadback, _occupancyBuffer);
        MappedSubresource mapped = _context.Map(_stagingReadback, MapMode.Read);
        try
        {
            var result = new uint[_cellCount];
            ReadUIntArray(mapped.DataPointer, result);
            return result;
        }
        finally
        {
            _context.Unmap(_stagingReadback);
        }
    }

    private unsafe void ReadUIntArray(nint ptr, uint[] dest)
    {
        fixed (uint* p = dest)
        {
            Buffer.MemoryCopy((void*)ptr, p, dest.Length * sizeof(uint), dest.Length * sizeof(uint));
        }
    }

    private void ApplyCutInternal(
        GpuCylinderCut cut,
        double worldMinX,
        double worldMinY,
        double worldMinZ,
        double resolutionMm)
    {
        var start = new Vector3((float)cut.StartX, (float)cut.StartY, (float)cut.StartZ);
        var end = new Vector3((float)cut.EndX, (float)cut.EndY, (float)cut.EndZ);
        Vector3 dir = end - start;
        double len2Xy = dir.X * dir.X + dir.Y * dir.Y;

        int minX = Math.Max(0, (int)((Math.Min(cut.StartX, cut.EndX) - cut.Radius - worldMinX) / resolutionMm));
        int maxX = Math.Min(_dimX - 1, (int)((Math.Max(cut.StartX, cut.EndX) + cut.Radius - worldMinX) / resolutionMm));
        int minY = Math.Max(0, (int)((Math.Min(cut.StartY, cut.EndY) - cut.Radius - worldMinY) / resolutionMm));
        int maxY = Math.Min(_dimY - 1, (int)((Math.Max(cut.StartY, cut.EndY) + cut.Radius - worldMinY) / resolutionMm));
        int minZ = Math.Max(0, (int)((Math.Min(cut.StartZ, cut.EndZ) - worldMinZ) / resolutionMm));
        int maxZ = Math.Min(_dimZ - 1, (int)((Math.Max(cut.StartZ, cut.EndZ) + cut.FluteLength - worldMinZ) / resolutionMm));

        int rx = Math.Max(0, maxX - minX + 1);
        int ry = Math.Max(0, maxY - minY + 1);
        int rz = Math.Max(0, maxZ - minZ + 1);
        if (rx <= 0 || ry <= 0 || rz <= 0)
        {
            return;
        }

        float radSq = (float)cut.Radius * (float)cut.Radius;
        float len2_xy = (float)len2Xy;

        var cutCb = new CutConstants
        {
            CornerMinVoxel = new Vector4((float)worldMinX, (float)worldMinY, (float)worldMinZ, (float)resolutionMm),
            MinIx = (uint)minX,
            MinIy = (uint)minY,
            MinIz = (uint)minZ,
            RangeX = (uint)rx,
            RangeY = (uint)ry,
            RangeZ = (uint)rz,
            DimXUInt = (uint)_dimX,
            DimYUInt = (uint)_dimY,
            DimZUInt = (uint)_dimZ,
            StartRadSq = new Vector4(start.X, start.Y, start.Z, radSq),
            DirLen2XY = new Vector4(dir.X, dir.Y, dir.Z, len2_xy),
            FlutePad = new Vector4((float)cut.FluteLength, 0, 0, 0)
        };

        _context.CSSetShader(_csCut);
        _context.CSSetUnorderedAccessViews(0u, new[] { _occupancyUav });

        unsafe
        {
            WriteConstantBuffer(&cutCb, sizeof(CutConstants));
        }

        long boxVolLong = (long)rx * ry * rz;
        int boxVol = (int)Math.Min(boxVolLong, int.MaxValue);
        int cutGroups = (boxVol + EmbeddedHlsl.ThreadsPerGroup - 1) / EmbeddedHlsl.ThreadsPerGroup;
        _context.Dispatch((uint)Math.Max(1, cutGroups), 1, 1);
    }

    private unsafe void WriteConstantBuffer<T>(in T data) where T : unmanaged
    {
        MappedSubresource map = _context.Map(_constantBufferDynamic, MapMode.WriteDiscard);
        try
        {
            Span<byte> span = new((void*)map.DataPointer, sizeof(T));
            MemoryMarshal.Write(span, in data);
        }
        finally
        {
            _context.Unmap(_constantBufferDynamic);
        }
    }

    private unsafe void WriteConstantBuffer(void* data, int size)
    {
        Debug.Assert(size <= 256);
        MappedSubresource map = _context.Map(_constantBufferDynamic, MapMode.WriteDiscard);
        try
        {
            Buffer.MemoryCopy(data, (void*)map.DataPointer, 256, size);
        }
        finally
        {
            _context.Unmap(_constantBufferDynamic);
        }
    }

    public void Dispose()
    {
        _constantBufferDynamic.Dispose();
        _stagingReadback.Dispose();
        _occupancyUav.Dispose();
        _occupancyBuffer.Dispose();
        _csInit.Dispose();
        _csCut.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    private struct InitConstants
    {
        public uint DimX;
        public uint DimY;
        public uint DimZ;
        public uint TotalCells;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    private struct CutConstants
    {
        public Vector4 CornerMinVoxel;

        public uint MinIx;
        public uint MinIy;
        public uint MinIz;
        public uint RangeX;

        public uint RangeY;
        public uint RangeZ;
        public uint DimXUInt;
        public uint DimYUInt;

        public uint DimZUInt;
        public uint Pad0;
        public uint Pad1;
        public uint Pad2;

        public Vector4 StartRadSq;
        public Vector4 DirLen2XY;
        public Vector4 FlutePad;
    }
}
