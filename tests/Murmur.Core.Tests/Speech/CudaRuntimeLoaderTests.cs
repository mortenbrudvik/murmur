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
        var path = string.Join(';', @"C:\Windows\System32", Cuda13, @"C:\Other\cuda13");

        CudaRuntimeLoader.FindRuntimeDirectory(path, Has(Cuda13, @"C:\Other\cuda13")).ShouldBe(Cuda13);
    }

    /// <summary>The CUDA 12 toolkit beside it (an older install the owner's PATH still lists) has no *_13 DLLs.</summary>
    [Fact]
    public void FindRuntimeDirectory_SkipsDirectoriesWithoutTheCuda13Runtime()
    {
        var path = string.Join(';', Cuda12, Cuda13);

        CudaRuntimeLoader.FindRuntimeDirectory(path, Has(Cuda13)).ShouldBe(Cuda13);
    }

    [Fact]
    public void FindRuntimeDirectory_SkipsADirectoryWithOnlySomeOfTheDlls()
    {
        var path = string.Join(';', @"C:\Partial", Cuda13);
        bool Exists(string file) =>
            file.StartsWith(Cuda13, StringComparison.OrdinalIgnoreCase)
            || file.Equals(@"C:\Partial\cudart64_13.dll", StringComparison.OrdinalIgnoreCase);

        CudaRuntimeLoader.FindRuntimeDirectory(path, Exists).ShouldBe(Cuda13);
    }

    [Fact]
    public void FindRuntimeDirectory_AcceptsQuotedEntriesAndIgnoresRelativeOnes()
    {
        var path = $@"bin;""{Cuda13}"" ; ";

        CudaRuntimeLoader.FindRuntimeDirectory(path, _ => true).ShouldBe(Cuda13);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ; ;")]
    public void FindRuntimeDirectory_NoUsablePath_ReturnsNull(string? path)
    {
        CudaRuntimeLoader.FindRuntimeDirectory(path, _ => true).ShouldBeNull();
    }

    [Fact]
    public void FindRuntimeDirectory_NothingOnPath_ReturnsNull()
    {
        CudaRuntimeLoader.FindRuntimeDirectory(string.Join(';', Cuda12, @"C:\Windows"), _ => false).ShouldBeNull();
    }

    /// <summary>Files that are there but are not loadable DLLs must leave the fallback to Vulkan or the CPU, not throw.</summary>
    [Fact]
    public void TryLoad_UnloadableFiles_ReturnsNull()
    {
        var directory = Path.Combine(Path.GetTempPath(), "murmur-cuda-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var dll in CudaRuntimeLoader.RuntimeDlls)
                File.WriteAllText(Path.Combine(directory, dll), "not a DLL");

            CudaRuntimeLoader.TryLoad(directory).ShouldBeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Func<string, bool> Has(params string[] directories) => file =>
        directories.Any(d => string.Equals(Path.GetDirectoryName(file), d, StringComparison.OrdinalIgnoreCase)
            && CudaRuntimeLoader.RuntimeDlls.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase));
}
