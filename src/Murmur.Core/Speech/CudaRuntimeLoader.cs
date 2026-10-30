using System.Runtime.InteropServices;
using Serilog;

namespace Murmur.Core.Speech;

/// <summary>
/// Loads the CUDA 13 runtime DLLs that Whisper.net's CUDA backend imports, by full path from the directory
/// <see cref="CudaRuntimeSearch"/> and <see cref="FindRuntimeDirectory"/> picked, before Whisper.net loads its native
/// runtime.
/// </summary>
/// <remarks>
/// An unpackaged process finds them on PATH by itself. A packaged one (the Microsoft Store build) does not: Windows'
/// DLL search order for packaged processes leaves PATH out, so ggml-cuda-whisper.dll cannot resolve its imports and
/// Whisper.net quietly falls back to Vulkan. Measured 2026-10-30 on one PC with one PATH: CUDA unpackaged, Vulkan
/// packaged, and inside the package ggml-cuda-whisper.dll loaded once these three had been loaded first. A module
/// that is already in the process satisfies a later import by name, which is what this relies on.
/// <para>
/// It runs in both channels so there is one code path: the directory it picks is one an unpackaged PATH search
/// would find, or one the user's PATH is about to hold after an install. Best effort and never throws: when the
/// runtime is missing or will not load, Whisper.net falls back to Vulkan or the CPU exactly as before.
/// </para>
/// </remarks>
internal static class CudaRuntimeLoader
{
    /// <summary>The imports of ggml-cuda-whisper.dll outside System32, in load order (cuBLAS needs cuBLASLt).</summary>
    internal static readonly string[] RuntimeDlls = ["cudart64_13.dll", "cublasLt64_13.dll", "cublas64_13.dll"];

    private static readonly ILogger Logger = Log.ForContext(typeof(CudaRuntimeLoader));

    /// <summary>Loads <see cref="RuntimeDlls"/> from <paramref name="directory"/>, in order; false when one will not load.</summary>
    public static bool TryLoad(string directory)
    {
        try
        {
            foreach (var dll in RuntimeDlls)
            {
                var path = Path.Combine(directory, dll);
                if (!NativeLibrary.TryLoad(path, out _))
                {
                    Logger.Warning("Could not load {Path}; the speech engine will use Vulkan or the CPU", path);
                    return false;
                }
            }

            Logger.Information("Loaded the CUDA 13 runtime from {Directory}", directory);
            return true;
        }
        catch (Exception ex)
        {
            // A preload is an optimisation: failing it must not fail the engine, which falls back on its own.
            Logger.Warning(ex, "Could not load the CUDA 13 runtime from {Directory}; the speech engine will use Vulkan or the CPU", directory);
            return false;
        }
    }

    /// <summary>
    /// The first of <paramref name="directories"/> that holds every one of <see cref="RuntimeDlls"/>, or null. A
    /// directory with only some of them is skipped. The directories must be fully qualified, as
    /// <see cref="CudaRuntimeSearch.Directories"/> returns them: a relative one would resolve against whatever the
    /// working directory is.
    /// </summary>
    internal static string? FindRuntimeDirectory(IEnumerable<string> directories, Func<string, bool> fileExists) =>
        directories.FirstOrDefault(directory => RuntimeDlls.All(dll => fileExists(Path.Combine(directory, dll))));
}
