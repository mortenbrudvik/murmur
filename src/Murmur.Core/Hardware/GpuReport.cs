namespace Murmur.Core.Hardware;

/// <summary>A hardware display adapter as DXGI lists it. Software adapters (Microsoft Basic Render Driver) are never listed.</summary>
/// <param name="Name">The DXGI description, such as "NVIDIA GeForce RTX 3090".</param>
/// <param name="VendorId">The PCI vendor ID: 0x10DE NVIDIA, 0x1002 AMD, 0x8086 Intel.</param>
/// <param name="DedicatedVideoMemory">Bytes of video memory not shared with the CPU.</param>
/// <param name="Luid">The adapter's locally unique identifier, packed as HighPart &lt;&lt; 32 | LowPart.</param>
public sealed record GpuAdapter(string Name, int VendorId, long DedicatedVideoMemory, long Luid);

/// <summary>A GPU as the NVIDIA driver's CUDA API lists it.</summary>
/// <param name="Luid">The LUID of the adapter this device is, packed like <see cref="GpuAdapter.Luid"/>; 0 when the driver gave none.</param>
public sealed record CudaDevice(long Luid, int ComputeMajor, int ComputeMinor);

/// <param name="Version">The highest CUDA version the installed driver supports, as 1000 × major + 10 × minor (13000 is 13.0).</param>
public sealed record CudaDriver(int Version, IReadOnlyList<CudaDevice> Devices);

/// <summary>What GPU detection found on this PC.</summary>
/// <param name="Cuda">Null when nvcuda.dll is missing (no NVIDIA driver) or the CUDA driver would not initialise.</param>
public sealed record GpuReport(IReadOnlyList<GpuAdapter> Adapters, CudaDriver? Cuda)
{
    public static GpuReport Empty { get; } = new([], null);
}
