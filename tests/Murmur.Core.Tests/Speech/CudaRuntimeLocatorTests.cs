using Murmur.Core.Services;
using Murmur.Core.Speech;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

/// <summary>Runs the real search on this PC; what it finds depends on whether the CUDA 13 runtime is installed.</summary>
public sealed class CudaRuntimeLocatorTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MurmurTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FindRuntimeDirectory_OnThisPc_FindsADirectoryWithEveryDllOrNothing()
    {
        var directory = new CudaRuntimeLocator(new MurmurPaths(_root)).FindRuntimeDirectory();
        output.WriteLine(directory ?? "(none)");

        if (directory is not null)
            CudaRuntimeLoader.RuntimeDlls.ShouldAllBe(dll => File.Exists(Path.Combine(directory, dll)));
    }

#if DEBUG
    [Fact]
    public void FindRuntimeDirectory_SimulateNoCudaRuntimeFlag_IsNull()
    {
        var paths = new MurmurPaths(_root);
        Directory.CreateDirectory(_root);
        File.WriteAllText(paths.SimulateNoCudaRuntimeFile, "");

        new CudaRuntimeLocator(paths).FindRuntimeDirectory().ShouldBeNull();
    }
#endif
}
