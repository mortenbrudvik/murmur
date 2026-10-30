namespace Murmur.Core.Hardware;

/// <summary>This PC's display adapters and NVIDIA CUDA driver.</summary>
public interface IGpuInfo
{
    /// <summary>
    /// Detected once, on first read, and the same object afterwards; safe to read from any thread. Never throws: a
    /// detection failure is logged and reads as <see cref="GpuReport.Empty"/>.
    /// </summary>
    GpuReport Report { get; }
}
