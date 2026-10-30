using Murmur.Core.Speech;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

public sealed class CudaRuntimeLoaderTests
{
    private const string Cuda13 = @"C:\CUDA\v13.2\bin\x64";
    private const string Cuda12 = @"C:\CUDA\v12.0\bin";

    [Fact]
    public void FindRuntimeDirectory_ReturnsTheFirstDirectoryHoldingEveryDll()
    {
        string[] directories = [@"C:\Windows\System32", Cuda13, @"C:\Other\cuda13"];

        CudaRuntimeLoader.FindRuntimeDirectory(directories, Has(Cuda13, @"C:\Other\cuda13")).ShouldBe(Cuda13);
    }

    /// <summary>The CUDA 12 toolkit beside it (an older install the owner's PATH still lists) has no *_13 DLLs.</summary>
    [Fact]
    public void FindRuntimeDirectory_SkipsDirectoriesWithoutTheCuda13Runtime()
    {
        CudaRuntimeLoader.FindRuntimeDirectory([Cuda12, Cuda13], Has(Cuda13)).ShouldBe(Cuda13);
    }

    [Fact]
    public void FindRuntimeDirectory_SkipsADirectoryWithOnlySomeOfTheDlls()
    {
        bool Exists(string file) =>
            file.StartsWith(Cuda13, StringComparison.OrdinalIgnoreCase)
            || file.Equals(@"C:\Partial\cudart64_13.dll", StringComparison.OrdinalIgnoreCase);

        CudaRuntimeLoader.FindRuntimeDirectory([@"C:\Partial", Cuda13], Exists).ShouldBe(Cuda13);
    }

    [Fact]
    public void FindRuntimeDirectory_NoDirectories_ReturnsNull()
    {
        CudaRuntimeLoader.FindRuntimeDirectory([], _ => true).ShouldBeNull();
    }

    [Fact]
    public void FindRuntimeDirectory_NothingHoldsTheDlls_ReturnsNull()
    {
        CudaRuntimeLoader.FindRuntimeDirectory([Cuda12, @"C:\Windows"], _ => false).ShouldBeNull();
    }

    /// <summary>Files that are there but are not loadable DLLs must leave the fallback to Vulkan or the CPU, not throw.</summary>
    [Fact]
    public void TryLoad_UnloadableFiles_ReturnsFalse()
    {
        var directory = Path.Combine(Path.GetTempPath(), "murmur-cuda-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var dll in CudaRuntimeLoader.RuntimeDlls)
                File.WriteAllText(Path.Combine(directory, dll), "not a DLL");

            CudaRuntimeLoader.TryLoad(directory).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryLoad_DirectoryWithoutTheDlls_ReturnsFalse()
    {
        var directory = Path.Combine(Path.GetTempPath(), "murmur-cuda-tests-" + Guid.NewGuid().ToString("N"));

        CudaRuntimeLoader.TryLoad(directory).ShouldBeFalse();
    }

    private static Func<string, bool> Has(params string[] directories) => file =>
        directories.Any(d => string.Equals(Path.GetDirectoryName(file), d, StringComparison.OrdinalIgnoreCase)
            && CudaRuntimeLoader.RuntimeDlls.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase));
}
