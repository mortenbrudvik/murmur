using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Murmur.Core.Interop;

// DXGI, just enough to list the display adapters. Each interface is flattened: the methods of its base interfaces come
// first, in vtable order (IUnknown's three are implied). The slots Murmur never calls are declared only to hold their
// place, so their signatures are not the real ones; calling one would be a bug.

/// <summary>IDXGIFactory1, from <see cref="NativeMethods.CreateDXGIFactory1"/>.</summary>
[GeneratedComInterface]
[Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
internal partial interface IDXGIFactory1
{
    // IDXGIObject (placeholders)
    void SetPrivateData();
    void SetPrivateDataInterface();
    void GetPrivateData();
    void GetParent();

    // IDXGIFactory (placeholders)
    void EnumAdapters();
    void MakeWindowAssociation();
    void GetWindowAssociation();
    void CreateSwapChain();
    void CreateSoftwareAdapter();

    // IDXGIFactory1

    /// <returns>S_OK, or <see cref="Dxgi.DXGI_ERROR_NOT_FOUND"/> when <paramref name="index"/> is past the last adapter.</returns>
    [PreserveSig]
    int EnumAdapters1(uint index, out IDXGIAdapter1? adapter);

    [PreserveSig]
    int IsCurrent();
}

[GeneratedComInterface]
[Guid("29038f61-3839-4626-91fd-086879011a05")]
internal unsafe partial interface IDXGIAdapter1
{
    // IDXGIObject (placeholders)
    void SetPrivateData();
    void SetPrivateDataInterface();
    void GetPrivateData();
    void GetParent();

    // IDXGIAdapter (placeholders)
    void EnumOutputs();
    void GetDesc();
    void CheckInterfaceSupport();

    // IDXGIAdapter1

    [PreserveSig]
    int GetDesc1(DXGI_ADAPTER_DESC1* desc);
}

internal static class Dxgi
{
    internal const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);

    /// <summary><see cref="DXGI_ADAPTER_DESC1.Flags"/>: the Microsoft Basic Render Driver, a CPU rasteriser.</summary>
    internal const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    internal const int DescriptionLength = 128;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DXGI_ADAPTER_DESC1
{
    public fixed char Description[Dxgi.DescriptionLength];
    public uint VendorId;
    public uint DeviceId;
    public uint SubSysId;
    public uint Revision;
    public nuint DedicatedVideoMemory;
    public nuint DedicatedSystemMemory;
    public nuint SharedSystemMemory;
    public LUID AdapterLuid;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;

    /// <summary>HighPart &lt;&lt; 32 | LowPart: the same number as the eight bytes of a LUID read as a little-endian long.</summary>
    public readonly long ToInt64() => ((long)HighPart << 32) | LowPart;
}
