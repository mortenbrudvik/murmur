using Murmur.Core.Models;
using Murmur.Core.Speech;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

/// <summary>
/// The same cases as <c>engineStatusText.test.ts</c> in the web app: the splash must say what the window says. A wording
/// change is made in both files, and in both test files.
/// </summary>
public sealed class EngineStatusTextTests
{
    private static EngineStatus Status(EngineState state, string? runtime = null, string? error = null,
        DownloadProgress? download = null, bool switching = false, string modelName = "Whisper large-v3-turbo") =>
        new(state, modelName, runtime, error, download, Switching: switching);

    private static DownloadProgress Download(long received, long total = 1_000_000_000, int retries = 0) =>
        new("Whisper large-v3-turbo", received, total, 0, null, retries);

    [Theory]
    [InlineData(999_999_999, "Downloading speech model… 99%")]
    [InlineData(380_000_000, "Downloading speech model… 38%")]
    [InlineData(0, "Downloading speech model… 0%")]
    [InlineData(-5, "Downloading speech model… 0%")]
    [InlineData(2_000_000_000, "Downloading speech model… 100%")]
    public void Describe_Downloading_ShowsTheWholePercent(long received, string expected)
    {
        EngineStatusText.Describe(Status(EngineState.Downloading, download: Download(received)))
            .ShouldBe(new EngineStatusLine(expected, EngineStatusTone.Progress));
    }

    [Fact]
    public void Describe_DownloadingWithNoDetailsOrNoTotal_ShowsZeroPercent()
    {
        EngineStatusText.Describe(Status(EngineState.Downloading)).Text.ShouldBe("Downloading speech model… 0%");
        EngineStatusText.Describe(Status(EngineState.Downloading, download: Download(5, total: 0))).Text
            .ShouldBe("Downloading speech model… 0%");
    }

    [Fact]
    public void Describe_DownloadReconnecting_SaysReconnecting()
    {
        EngineStatusText.Describe(Status(EngineState.Downloading, download: Download(380_000_000, retries: 1)))
            .ShouldBe(new EngineStatusLine("Reconnecting…", EngineStatusTone.Progress));
    }

    [Theory]
    [InlineData(EngineState.Loading)]
    [InlineData(EngineState.NotStarted)]
    public void Describe_LoadingOrNotStarted_SaysLoadingModel(EngineState state)
    {
        EngineStatusText.Describe(Status(state)).ShouldBe(new EngineStatusLine("Loading model…", EngineStatusTone.Info));
    }

    [Fact]
    public void Describe_Switching_NamesTheModelBeingLoaded()
    {
        EngineStatusText.Describe(Status(EngineState.Loading, switching: true, modelName: "Whisper small (English)"))
            .ShouldBe(new EngineStatusLine("Switching to Whisper small (English)…", EngineStatusTone.Info));
    }

    [Theory]
    [InlineData("Cuda", "Ready · CUDA")]
    [InlineData("Vulkan", "Ready · Vulkan")]
    [InlineData("Cpu", "Ready · CPU")]
    [InlineData("Metal", "Ready · Metal")]
    [InlineData(null, "Ready")]
    [InlineData("", "Ready")]
    public void Describe_Ready_NamesTheRuntime(string? runtime, string expected)
    {
        EngineStatusText.Describe(Status(EngineState.Ready, runtime)).ShouldBe(new EngineStatusLine(expected, EngineStatusTone.Ready));
    }

    [Fact]
    public void Describe_ReadyWhileAModelSwitchDownloads_StaysReady()
    {
        EngineStatusText.Describe(Status(EngineState.Ready, "Cuda", download: Download(380_000_000))).Text.ShouldBe("Ready · CUDA");
    }

    [Theory]
    [InlineData("The disk is full.", "The disk is full.")]
    [InlineData(null, "The speech model could not be loaded.")]
    [InlineData("", "The speech model could not be loaded.")]
    public void Describe_Error_ShowsTheErrorOrAFallback(string? error, string expected)
    {
        EngineStatusText.Describe(Status(EngineState.Error, error: error)).ShouldBe(new EngineStatusLine(expected, EngineStatusTone.Error));
    }
}
