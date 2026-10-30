using Murmur.Core.Services;
using Serilog;

namespace Murmur.Core.Speech;

/// <summary>Searches the process PATH, the registry's Path values, CUDA_PATH and the toolkit's install folder.</summary>
/// <param name="paths">Debug builds look for <see cref="MurmurPaths.SimulateNoCudaRuntimeFile"/> in it.</param>
public sealed class CudaRuntimeLocator(MurmurPaths paths) : ICudaRuntimeLocator
{
    private readonly ILogger _logger = Log.ForContext<CudaRuntimeLocator>();

    public string? FindRuntimeDirectory()
    {
        if (SimulatesNoRuntime(paths))
            return null;

        try
        {
            return CudaRuntimeLoader.FindRuntimeDirectory(CudaRuntimeSearch.Directories(CudaRuntimeSearch.FromSystem()), File.Exists);
        }
        catch (Exception ex)
        {
            // Looking is best effort: failing it must read as "not installed", never stop Murmur.
            _logger.Warning(ex, "Could not look for the CUDA 13 runtime; carrying on as if it is not installed");
            return null;
        }
    }

    private bool SimulatesNoRuntime(MurmurPaths paths)
    {
#if DEBUG
        // For checking the no-CUDA-runtime experience end to end on a PC that has it.
        if (File.Exists(paths.SimulateNoCudaRuntimeFile))
        {
            _logger.Warning("Simulating a PC without the CUDA 13 runtime because {Path} exists", paths.SimulateNoCudaRuntimeFile);
            return true;
        }
#endif
        return false;
    }
}
