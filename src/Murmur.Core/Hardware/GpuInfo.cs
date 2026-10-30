using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Murmur.Core.Interop;
using Murmur.Core.Services;
using Serilog;

namespace Murmur.Core.Hardware;

/// <summary>
/// Lists the display adapters through DXGI and asks the NVIDIA driver (nvcuda.dll, in System32 wherever the driver is
/// installed; never shipped with Murmur) for its CUDA version and each GPU's compute capability.
/// </summary>
public sealed class GpuInfo : IGpuInfo
{
    private const int CudaSuccess = 0;
    private const int CuDeviceAttributeComputeCapabilityMajor = 75;
    private const int CuDeviceAttributeComputeCapabilityMinor = 76;

    private readonly ILogger _logger = Log.ForContext<GpuInfo>();
    private readonly Lazy<GpuReport> _report;

    /// <param name="paths">Debug builds look for <see cref="MurmurPaths.SimulateNoGpuFile"/> in it.</param>
    public GpuInfo(MurmurPaths paths)
    {
        _report = new Lazy<GpuReport>(() => SimulatesNoGpu(paths) ? GpuReport.Empty : Detect(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public GpuReport Report => _report.Value;

    /// <summary>The detection result as one readable line, for the log.</summary>
    internal static string Describe(GpuReport report)
    {
        var adapters = report.Adapters.Count == 0
            ? "none"
            : string.Join(", ", report.Adapters.Select(a => string.Create(CultureInfo.InvariantCulture,
                $"{a.Name} (vendor 0x{a.VendorId:X4}, {a.DedicatedVideoMemory / (double)(1L << 30):F1} GiB, LUID 0x{a.Luid:X})")));

        if (report.Cuda is not { } cuda)
            return $"GPU adapters: {adapters}; no CUDA driver";

        var devices = cuda.Devices.Count == 0
            ? "none"
            : string.Join(", ", cuda.Devices.Select(d => string.Create(CultureInfo.InvariantCulture,
                $"compute capability {d.ComputeMajor}.{d.ComputeMinor} (LUID 0x{d.Luid:X})")));
        return string.Create(CultureInfo.InvariantCulture,
            $"GPU adapters: {adapters}; CUDA driver {cuda.Version / 1000}.{cuda.Version % 1000 / 10} ({cuda.Version}), devices: {devices}");
    }

    private bool SimulatesNoGpu(MurmurPaths paths)
    {
#if DEBUG
        // For checking the no-GPU experience end to end on a PC that has one.
        if (File.Exists(paths.SimulateNoGpuFile))
        {
            _logger.Warning("Simulating a PC without a GPU because {Path} exists", paths.SimulateNoGpuFile);
            return true;
        }
#endif
        return false;
    }

    private GpuReport Detect()
    {
        try
        {
            var report = new GpuReport(ReadAdapters(), ReadCudaDriver());
            _logger.Information("Detected {GpuSummary}", Describe(report));
            return report;
        }
        catch (Exception ex)
        {
            // Detection only steers advice and defaults; failing it must never stop Murmur.
            _logger.Warning(ex, "GPU detection failed; carrying on as if this PC had no GPU");
            return GpuReport.Empty;
        }
    }

    /// <summary>The hardware adapters in DXGI order (the primary display's adapter first), software adapters left out.</summary>
    private static unsafe List<GpuAdapter> ReadAdapters()
    {
        var adapters = new List<GpuAdapter>();
        Marshal.ThrowExceptionForHR(NativeMethods.CreateDXGIFactory1(typeof(IDXGIFactory1).GUID, out var factory));
        try
        {
            for (uint index = 0; ; index++)
            {
                var hr = factory.EnumAdapters1(index, out var adapter);
                if (hr == Dxgi.DXGI_ERROR_NOT_FOUND)
                    return adapters;
                Marshal.ThrowExceptionForHR(hr);

                try
                {
                    DXGI_ADAPTER_DESC1 desc;
                    Marshal.ThrowExceptionForHR(adapter!.GetDesc1(&desc));
                    if ((desc.Flags & Dxgi.DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
                        continue;

                    var name = new ReadOnlySpan<char>(desc.Description, Dxgi.DescriptionLength);
                    var end = name.IndexOf('\0');
                    adapters.Add(new GpuAdapter(
                        (end >= 0 ? name[..end] : name).Trim().ToString(),
                        (int)desc.VendorId,
                        (long)desc.DedicatedVideoMemory,
                        desc.AdapterLuid.ToInt64()));
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
    }

    /// <summary>The driver's CUDA version and devices, or null without nvcuda.dll or when the driver will not initialise.</summary>
    private static unsafe CudaDriver? ReadCudaDriver()
    {
        // System32 only: the driver puts nvcuda.dll there, and a copy anywhere else is not the driver's.
        if (!NativeLibrary.TryLoad("nvcuda.dll", typeof(GpuInfo).Assembly, DllImportSearchPath.System32, out var library))
            return null;

        try
        {
            if (!NativeLibrary.TryGetExport(library, "cuInit", out var cuInitAddress)
                || !NativeLibrary.TryGetExport(library, "cuDriverGetVersion", out var cuDriverGetVersionAddress)
                || !NativeLibrary.TryGetExport(library, "cuDeviceGetCount", out var cuDeviceGetCountAddress)
                || !NativeLibrary.TryGetExport(library, "cuDeviceGet", out var cuDeviceGetAddress)
                || !NativeLibrary.TryGetExport(library, "cuDeviceGetAttribute", out var cuDeviceGetAttributeAddress)
                || !NativeLibrary.TryGetExport(library, "cuDeviceGetLuid", out var cuDeviceGetLuidAddress))
                return null;

            var cuInit = (delegate* unmanaged[Stdcall]<uint, int>)cuInitAddress;
            var cuDriverGetVersion = (delegate* unmanaged[Stdcall]<int*, int>)cuDriverGetVersionAddress;
            var cuDeviceGetCount = (delegate* unmanaged[Stdcall]<int*, int>)cuDeviceGetCountAddress;
            var cuDeviceGet = (delegate* unmanaged[Stdcall]<int*, int, int>)cuDeviceGetAddress;
            var cuDeviceGetAttribute = (delegate* unmanaged[Stdcall]<int*, int, int, int>)cuDeviceGetAttributeAddress;
            var cuDeviceGetLuid = (delegate* unmanaged[Stdcall]<byte*, uint*, int, int>)cuDeviceGetLuidAddress;

            int version;
            if (cuInit(0) != CudaSuccess || cuDriverGetVersion(&version) != CudaSuccess)
                return null;

            int count;
            if (cuDeviceGetCount(&count) != CudaSuccess)
                count = 0;

            var devices = new List<CudaDevice>(count);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                int device, major, minor;
                if (cuDeviceGet(&device, ordinal) != CudaSuccess
                    || cuDeviceGetAttribute(&major, CuDeviceAttributeComputeCapabilityMajor, device) != CudaSuccess
                    || cuDeviceGetAttribute(&minor, CuDeviceAttributeComputeCapabilityMinor, device) != CudaSuccess)
                    continue;

                // Eight bytes, LowPart first; read as a little-endian long they pack like LUID.ToInt64. A device
                // without one (TCC mode, no display adapter to match) keeps 0.
                long luid = 0;
                uint nodeMask;
                if (cuDeviceGetLuid((byte*)&luid, &nodeMask, device) != CudaSuccess)
                    luid = 0;

                devices.Add(new CudaDevice(luid, major, minor));
            }

            return new CudaDriver(version, devices);
        }
        finally
        {
            // cuInit leaves the driver initialised in this process, which does no harm. Measured 2026-10-30 (RTX 3090,
            // CUDA 13.2 driver): nvcuda.dll stays loaded after this Free, so the thread cuInit starts never outlives it.
            NativeLibrary.Free(library);
        }
    }

    /// <summary>Releases the COM object now rather than whenever the finalizer runs.</summary>
    private static void Release(object? comObject) => (comObject as ComObject)?.FinalRelease();
}
