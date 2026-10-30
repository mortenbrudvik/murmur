using Murmur.Core.Models;

namespace Murmur.Core.Hardware;

/// <summary>Decides whether this PC can run Murmur's CUDA build, and which tip about CUDA the user should see.</summary>
public static class GpuAdvisor
{
    public const int NvidiaVendorId = 0x10DE;

    /// <summary>CUDA 13.0, in <see cref="CudaDriver.Version"/>'s encoding: the CUDA build needs a driver that supports it.</summary>
    public const int MinCudaDriverVersion = 13000;

    // Compute capability 8.6 (RTX 30 series) and newer: ggml-cuda-whisper.dll in Whisper.net.Runtime.Cuda 1.9.1 has
    // native code for sm_86, sm_89, sm_120a and sm_121a only; older GPUs get PTX (unmeasured JIT) or nothing at all.
    // Re-check whenever the Whisper.net version changes.
    private const int MinComputeMajor = 8;
    private const int MinComputeMinor = 6;

    /// <summary>
    /// The first NVIDIA adapter whose CUDA device has compute capability 8.6 or newer, or null. Devices are matched to
    /// adapters by LUID, except that a PC with exactly one NVIDIA adapter and one CUDA device pairs them regardless.
    /// </summary>
    public static GpuAdapter? FindSupportedNvidia(GpuReport report)
    {
        if (report.Cuda is not { Devices: var devices })
            return null;

        var nvidia = report.Adapters.Where(a => a.VendorId == NvidiaVendorId).ToList();
        if (nvidia.Count == 1 && devices.Count == 1)
            return IsSupported(devices[0]) ? nvidia[0] : null;

        return nvidia.FirstOrDefault(adapter => devices.Any(d => d.Luid == adapter.Luid && IsSupported(d)));
    }

    /// <summary>True when there is a supported NVIDIA GPU and its driver supports CUDA 13.</summary>
    public static bool CanUseCuda(GpuReport report) =>
        FindSupportedNvidia(report) is not null && report.Cuda!.Version >= MinCudaDriverVersion;

    /// <summary>
    /// The tip about CUDA, by the first rule that applies: CUDA is in use; no supported NVIDIA GPU (nothing to say);
    /// the driver is too old for CUDA 13; the CUDA runtime is missing; otherwise nothing, which covers "found but not
    /// loaded yet" and "found, but the engine ended on Vulkan or the CPU".
    /// </summary>
    /// <param name="runtimeFound">The CUDA 13 runtime DLLs were found on PATH.</param>
    /// <param name="loadedRuntime">The runtime the speech engine loaded ("Cuda", "Vulkan", "Cpu"); null before it has.</param>
    public static GpuAdvice Decide(GpuReport report, bool runtimeFound, string? loadedRuntime)
    {
        var adapter = FindSupportedNvidia(report);

        if (loadedRuntime?.StartsWith("Cuda", StringComparison.Ordinal) == true)
            return new GpuAdvice(GpuAdviceState.CudaInUse, adapter?.Name);
        if (adapter is null)
            return GpuAdvice.None;
        if (report.Cuda!.Version < MinCudaDriverVersion)
            return new GpuAdvice(GpuAdviceState.UpdateDriver, adapter.Name);
        if (!runtimeFound)
            return new GpuAdvice(GpuAdviceState.InstallCuda, adapter.Name);
        return GpuAdvice.None;
    }

    private static bool IsSupported(CudaDevice device) =>
        device.ComputeMajor > MinComputeMajor
        || (device.ComputeMajor == MinComputeMajor && device.ComputeMinor >= MinComputeMinor);
}
