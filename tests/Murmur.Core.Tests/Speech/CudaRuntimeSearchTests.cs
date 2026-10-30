using Murmur.Core.Speech;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

public sealed class CudaRuntimeSearchTests
{
    private const string Toolkit = @"C:\PF\NVIDIA GPU Computing Toolkit\CUDA";

    private static CudaSearchSources Sources(
        string? processPath = null, string? machinePath = null, string? userPath = null, string? cudaPath = null,
        string? programFiles = null, Func<string, IEnumerable<string>>? listDirectories = null) =>
        new(processPath, machinePath, userPath, cudaPath, programFiles, listDirectories ?? (_ => []));

    [Fact]
    public void Directories_FollowTheSpecOrder()
    {
        var sources = Sources(@"C:\p1", @"C:\m1", @"C:\u1", @"C:\cuda", @"C:\PF",
            _ => [$@"{Toolkit}\v13.0", $@"{Toolkit}\v13.2", $@"{Toolkit}\v12.8"]);

        CudaRuntimeSearch.Directories(sources).ShouldBe(
        [
            @"C:\p1", @"C:\m1", @"C:\u1",
            @"C:\cuda\bin\x64", @"C:\cuda\bin",
            $@"{Toolkit}\v13.2\bin\x64", $@"{Toolkit}\v13.2\bin",
            $@"{Toolkit}\v13.0\bin\x64", $@"{Toolkit}\v13.0\bin",
        ]);
    }

    /// <summary>"v13.10" is newer than "v13.9", which a text comparison would get backwards.</summary>
    [Fact]
    public void Directories_ToolkitFolders_AreOrderedByTheNumberAfterV13()
    {
        var sources = Sources(programFiles: @"C:\PF", listDirectories: _ => [$@"{Toolkit}\v13.9", $@"{Toolkit}\v13.10"]);

        CudaRuntimeSearch.Directories(sources).ShouldBe(
        [
            $@"{Toolkit}\v13.10\bin\x64", $@"{Toolkit}\v13.10\bin",
            $@"{Toolkit}\v13.9\bin\x64", $@"{Toolkit}\v13.9\bin",
        ]);
    }

    [Fact]
    public void Directories_ToolkitFolders_AskTheToolkitFolderUnderProgramFiles()
    {
        var asked = new List<string>();
        var sources = Sources(programFiles: @"C:\PF", listDirectories: path =>
        {
            asked.Add(path);
            return [];
        });

        CudaRuntimeSearch.Directories(sources);

        asked.ShouldBe([Toolkit]);
    }

    [Fact]
    public void Directories_ToolkitFoldersThatAreNotV13_AreIgnored()
    {
        var sources = Sources(programFiles: @"C:\PF", listDirectories: _ =>
            [$@"{Toolkit}\v13.x", $@"{Toolkit}\v13.", $@"{Toolkit}\v130.1", $@"{Toolkit}\V13.1"]);

        CudaRuntimeSearch.Directories(sources).ShouldBe(
            [$@"{Toolkit}\V13.1\bin\x64", $@"{Toolkit}\V13.1\bin"]);
    }

    [Fact]
    public void Directories_RemoveDuplicatesKeepingTheFirst()
    {
        var sources = Sources(processPath: @"C:\x\", userPath: @"c:\X");

        CudaRuntimeSearch.Directories(sources).ShouldBe([@"C:\x\"]);
    }

    [Fact]
    public void Directories_ADirectoryOnPathAndInTheToolkit_IsListedOnce()
    {
        var sources = Sources(processPath: $@"{Toolkit}\v13.2\bin", programFiles: @"C:\PF",
            listDirectories: _ => [$@"{Toolkit}\v13.2"]);

        CudaRuntimeSearch.Directories(sources).ShouldBe([$@"{Toolkit}\v13.2\bin", $@"{Toolkit}\v13.2\bin\x64"]);
    }

    [Fact]
    public void Directories_AcceptQuotedEntriesAndDropRelativeOnes()
    {
        var sources = Sources(processPath: $@"bin;""C:\q1"" ; ;..\up;C:\q2", machinePath: @"""C:\q3""", cudaPath: @"cuda");

        CudaRuntimeSearch.Directories(sources).ShouldBe([@"C:\q1", @"C:\q2", @"C:\q3"]);
    }

    [Fact]
    public void Directories_AllSourcesMissing_IsEmpty()
    {
        CudaRuntimeSearch.Directories(Sources()).ShouldBeEmpty();
    }

    [Fact]
    public void Directories_ListingProgramFilesThrows_SkipsIt()
    {
        var sources = Sources(@"C:\p1", @"C:\m1", @"C:\u1", @"C:\cuda", @"C:\PF",
            _ => throw new DirectoryNotFoundException());

        CudaRuntimeSearch.Directories(sources).ShouldBe([@"C:\p1", @"C:\m1", @"C:\u1", @"C:\cuda\bin\x64", @"C:\cuda\bin"]);
    }

    /// <summary>A lazy enumeration fails on the first step, not on the call that returns it.</summary>
    [Fact]
    public void Directories_EnumeratingProgramFilesThrows_SkipsIt()
    {
        static IEnumerable<string> Failing()
        {
            yield return $@"{Toolkit}\v13.2";
            throw new UnauthorizedAccessException();
        }

        var sources = Sources(@"C:\p1", programFiles: @"C:\PF", listDirectories: _ => Failing());

        CudaRuntimeSearch.Directories(sources).ShouldBe([@"C:\p1"]);
    }

    [Fact]
    public void Directories_WithoutProgramFiles_DoesNotListAnything()
    {
        var sources = Sources(@"C:\p1", listDirectories: _ => throw new InvalidOperationException("must not be called"));

        CudaRuntimeSearch.Directories(sources).ShouldBe([@"C:\p1"]);
    }

    /// <summary>Runs the real registry and file system reads on this PC; what they find depends on the PC.</summary>
    [Fact]
    public void FromSystem_OnThisPc_DoesNotThrow()
    {
        var sources = CudaRuntimeSearch.FromSystem();

        sources.ProgramFiles.ShouldNotBeNullOrWhiteSpace();
        CudaRuntimeSearch.Directories(sources).ShouldAllBe(d => Path.IsPathFullyQualified(d));
    }
}
