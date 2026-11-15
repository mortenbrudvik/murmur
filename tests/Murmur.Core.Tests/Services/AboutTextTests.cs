using System.Globalization;
using Murmur.Core.Models;
using Murmur.Core.Services;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Services;

public sealed class AboutTextTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static readonly EngineStatus Ready = new(
        EngineState.Ready, "Whisper large-v3-turbo", "Cuda", Gpu: new GpuAdvice(GpuAdviceState.CudaInUse, "NVIDIA GeForce RTX 3090"));

    private static readonly HistoryTotals Totals = new(1204, 18420, new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void RunningOn_Cuda_NamesTheGraphicsCard()
    {
        AboutText.RunningOn(Ready).ShouldBe("NVIDIA GeForce RTX 3090 · CUDA");
    }

    [Fact]
    public void RunningOn_CudaWithoutACardName_SaysJustCuda()
    {
        AboutText.RunningOn(Ready with { Gpu = null }).ShouldBe("CUDA");
        AboutText.RunningOn(Ready with { Gpu = new GpuAdvice(GpuAdviceState.CudaInUse, null) }).ShouldBe("CUDA");
    }

    /// <summary>The status's card is the CUDA tip's, not the device in use: on Vulkan nothing says which card does the work.</summary>
    [Fact]
    public void RunningOn_Vulkan_DoesNotNameACard()
    {
        var vulkan = Ready with { Runtime = "Vulkan", Gpu = new GpuAdvice(GpuAdviceState.InstallCuda, "NVIDIA GeForce RTX 3060") };

        AboutText.RunningOn(vulkan).ShouldBe("Vulkan");
    }

    [Fact]
    public void RunningOn_Cpu_SaysCpu()
    {
        AboutText.RunningOn(Ready with { Runtime = "Cpu", Gpu = GpuAdvice.None }).ShouldBe("CPU");
    }

    [Theory]
    [InlineData(EngineState.NotStarted)]
    [InlineData(EngineState.Downloading)]
    [InlineData(EngineState.Loading)]
    public void RunningOn_EngineNotReadyYet_SaysStarting(EngineState state)
    {
        AboutText.RunningOn(Ready with { State = state, Runtime = null }).ShouldBe("Starting…");
    }

    [Fact]
    public void RunningOn_NoStatusYet_SaysStarting()
    {
        AboutText.RunningOn(null).ShouldBe("Starting…");
    }

    [Fact]
    public void RunningOn_EngineFailed_SaysNotRunning()
    {
        AboutText.RunningOn(Ready with { State = EngineState.Error, Runtime = null, Error = "Out of memory" }).ShouldBe("Not running");
    }

    [Fact]
    public void Dictated_EmptyHistory_SaysNothingYet()
    {
        AboutText.Dictated(new HistoryTotals(0, 0, null), English).ShouldBe(("Nothing yet", null));
    }

    [Fact]
    public void Dictated_GivesTheWordsTheDictationsAndTheMonthTheHistoryStartsIn()
    {
        AboutText.Dictated(Totals, English).ShouldBe(("18,420 words", "in 1,204 dictations since October 2026"));
    }

    [Fact]
    public void Dictated_OneWordAndOneDictation_UsesTheSingular()
    {
        AboutText.Dictated(Totals with { Dictations = 1, Words = 1 }, English)
            .ShouldBe(("1 word", "in 1 dictation since October 2026"));
    }

    /// <summary>Digits are grouped the way the user's culture does; the month is English, like the rest of the UI.</summary>
    [Fact]
    public void Dictated_AnotherCulture_GroupsDigitsItsWayAndKeepsTheMonthInEnglish()
    {
        AboutText.Dictated(Totals, CultureInfo.GetCultureInfo("de-DE"))
            .ShouldBe(("18.420 words", "in 1.204 dictations since October 2026"));
    }

    [Fact]
    public void Details_ListsVersionAndChannelModelRuntimeWithTheCardAndTheWindowsBuild()
    {
        AboutText.Details(App(), Ready).ShouldBe(string.Join(
            "\n",
            "Murmur 1.2.0 (Microsoft Store)",
            "Model: Whisper large-v3-turbo",
            "Runtime: CUDA, NVIDIA GeForce RTX 3090",
            "Windows 11, build 26200"));
    }

    [Fact]
    public void Details_NoCardToName_GivesTheBareRuntime()
    {
        AboutText.Details(App(), Ready with { Runtime = "Vulkan" }).ShouldContain("\nRuntime: Vulkan\n");
        AboutText.Details(App(), Ready with { Runtime = "Cpu" }).ShouldContain("\nRuntime: CPU\n");
        AboutText.Details(App(), Ready with { Gpu = null }).ShouldContain("\nRuntime: CUDA\n");
    }

    [Fact]
    public void Details_EngineHasNoModelYet_SaysStartingAndNotChosenYet()
    {
        var details = AboutText.Details(App(), new EngineStatus(EngineState.NotStarted, ""));

        details.ShouldContain("\nModel: Not chosen yet\n");
        details.ShouldContain("\nRuntime: Starting\n");
    }

    [Fact]
    public void Details_EngineFailedOrNoStatus_SaysSo()
    {
        AboutText.Details(App(), Ready with { State = EngineState.Error, Runtime = null }).ShouldContain("\nRuntime: Not running\n");
        AboutText.Details(App(), null).ShouldContain("\nModel: Not chosen yet\nRuntime: Starting\n");
    }

    [Theory]
    [InlineData("Whisper large-v3-turbo", "Whisper large-v3-turbo")]
    [InlineData("", "Not chosen yet")]
    public void SpeechModel_NamesTheModelOnceThereIsOne(string modelName, string expected)
    {
        AboutText.SpeechModel(new EngineStatus(EngineState.Loading, modelName)).ShouldBe(expected);
    }

    [Fact]
    public void SpeechModel_NoStatusYet_SaysNotChosenYet()
    {
        AboutText.SpeechModel(null).ShouldBe("Not chosen yet");
    }

    private static IAppInfo App()
    {
        var app = Substitute.For<IAppInfo>();
        app.Version.Returns("1.2.0");
        app.Channel.Returns("Microsoft Store");
        app.WindowsBuild.Returns("Windows 11, build 26200");
        return app;
    }
}
