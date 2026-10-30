using System.Runtime.InteropServices;
using Serilog;

namespace Murmur.Core.Speech;

/// <summary>
/// Loads the CUDA 13 runtime DLLs that Whisper.net's CUDA backend imports, by full path from the first PATH directory
/// that has all of them, before Whisper.net loads its native runtime.
/// </summary>
/// <remarks>
/// An unpackaged process finds them on PATH by itself. A packaged one (the Microsoft Store build) does not: Windows'
/// DLL search order for packaged processes leaves PATH out, so ggml-cuda-whisper.dll cannot resolve its imports and
/// Whisper.net quietly falls back to Vulkan. Measured 2026-10-30 on one PC with one PATH: CUDA unpackaged, Vulkan
/// packaged, and inside the package ggml-cuda-whisper.dll loaded once these three had been loaded first. A module
/// that is already in the process satisfies a later import by name, which is what this relies on.
/// <para>
/// It runs in both channels so there is one code path: the directory it picks is the one an unpackaged PATH search
/// finds anyway. Best effort and never throws: when the runtime is missing or will not load, Whisper.net falls back to
/// Vulkan or the CPU exactly as before.
/// </para>
/// </remarks>
internal static class CudaRuntimeLoader
{
    /// <summary>The imports of ggml-cuda-whisper.dll outside System32, in load order (cuBLAS needs cuBLASLt).</summary>
    internal static readonly string[] RuntimeDlls = ["cudart64_13.dll", "cublasLt64_13.dll", "cublas64_13.dll"];

    private static readonly ILogger Logger = Log.ForContext(typeof(CudaRuntimeLoader));

    /// <summary>Loads the runtime from <paramref name="searchPath"/> (a PATH value); returns the directory, or null.</summary>
    public static string? TryLoad(string? searchPath)
    {
        try
        {
            var directory = FindRuntimeDirectory(searchPath, File.Exists);
            if (directory is null)
            {
                Logger.Information("No CUDA 13 runtime on PATH; the speech engine will use Vulkan or the CPU");
                return null;
            }

            foreach (var dll in RuntimeDlls)
            {
                var path = Path.Combine(directory, dll);
                if (!NativeLibrary.TryLoad(path, out _))
                {
                    Logger.Warning("Could not load {Path}; the speech engine will use Vulkan or the CPU", path);
                    return null;
                }
            }

            Logger.Information("Loaded the CUDA 13 runtime from {Directory}", directory);
            return directory;
        }
        catch (Exception ex)
        {
            // A preload is an optimisation: failing it must not fail the engine, which falls back on its own.
            Logger.Warning(ex, "Could not look for the CUDA 13 runtime; the speech engine will use Vulkan or the CPU");
            return null;
        }
    }

    /// <summary>
    /// The first fully qualified directory in <paramref name="searchPath"/> that holds every one of
    /// <see cref="RuntimeDlls"/>, or null. A directory with only some of them is skipped.
    /// </summary>
    internal static string? FindRuntimeDirectory(string? searchPath, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(searchPath))
            return null;

        var entries = searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            // PATH entries may be quoted; a relative one would resolve against whatever the working directory is.
            var directory = entry.Trim('"');
            if (!Path.IsPathFullyQualified(directory))
                continue;

            if (RuntimeDlls.All(dll => fileExists(Path.Combine(directory, dll))))
                return directory;
        }

        return null;
    }
}
