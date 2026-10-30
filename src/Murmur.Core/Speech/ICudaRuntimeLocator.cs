namespace Murmur.Core.Speech;

/// <summary>Finds the directory that holds the CUDA 13 runtime DLLs on this PC, if any.</summary>
public interface ICudaRuntimeLocator
{
    /// <summary>
    /// The first directory that holds all three DLLs, searched afresh on every call, so an install made since the last
    /// call is found; null when there is none. Never throws.
    /// </summary>
    string? FindRuntimeDirectory();
}
