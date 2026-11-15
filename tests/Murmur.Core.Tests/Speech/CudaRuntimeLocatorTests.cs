using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.Core.Tests.Helpers;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

/// <summary>Runs the real search on this PC; what it finds depends on whether the CUDA 13 runtime is installed.</summary>
public sealed class CudaRuntimeLocatorTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void FindRuntimeDirectory_OnThisPc_FindsADirectoryWithEveryDllOrNothing()
    {
        var directory = new CudaRuntimeLocator(new MurmurPaths(_temp.Path)).FindRuntimeDirectory();
        output.WriteLine(directory ?? "(none)");

        if (directory is not null)
            CudaRuntimeLoader.RuntimeDlls.ShouldAllBe(dll => File.Exists(Path.Combine(directory, dll)));
    }

#if DEBUG
    [Fact]
    public void FindRuntimeDirectory_SimulateNoCudaRuntimeFlag_IsNull()
    {
        var paths = new MurmurPaths(_temp.Path);
        Directory.CreateDirectory(_temp.Path);
        File.WriteAllText(paths.SimulateNoCudaRuntimeFile, "");

        new CudaRuntimeLocator(paths).FindRuntimeDirectory().ShouldBeNull();
    }
#endif
}
