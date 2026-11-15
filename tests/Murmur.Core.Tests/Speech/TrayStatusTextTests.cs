using Murmur.Core.Models;
using Murmur.Core.Speech;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

public class TrayStatusTextTests
{
    private const string Model = "Whisper large-v3-turbo";

    [Fact]
    public void ToolTip_Ready_NamesTheHotkey()
    {
        TrayStatusText.ToolTip(new EngineStatus(EngineState.Ready, Model, "Cuda"), "Right Ctrl")
            .ShouldBe("Murmur — hold Right Ctrl to talk");
    }

    [Fact]
    public void ToolTip_HookUnavailable_SaysTheHotkeyIsOff()
    {
        TrayStatusText.ToolTip(new EngineStatus(EngineState.Ready, Model, "Cuda"), "Right Ctrl", hookInstalled: false)
            .ShouldBe("Murmur — the hotkey isn't working");
    }

    /// <summary>The old model dictates while the next one loads, so the hotkey not working is the news then too.</summary>
    [Fact]
    public void ToolTip_HookUnavailableWhileSwitchingModels_SaysTheHotkeyIsOff()
    {
        var status = new EngineStatus(EngineState.Loading, "Whisper small (English)", "Cuda", Switching: true);

        TrayStatusText.ToolTip(status, "Right Ctrl", hookInstalled: false).ShouldBe("Murmur — the hotkey isn't working");
    }

    /// <summary>Only the line that advertises the hotkey changes; what the engine is doing is still the news.</summary>
    [Fact]
    public void ToolTip_HookUnavailableWhileTheEngineLoads_StillSaysLoading()
    {
        TrayStatusText.ToolTip(new EngineStatus(EngineState.Loading, Model), "Right Ctrl", hookInstalled: false)
            .ShouldBe("Murmur — loading model…");
    }

    /// <summary>The old model keeps dictating while the next one loads, so this is not "loading model…".</summary>
    [Fact]
    public void ToolTip_SwitchingModels_NamesTheModelBeingLoaded()
    {
        var status = new EngineStatus(EngineState.Loading, "Whisper small (English)", "Cuda", Switching: true);

        TrayStatusText.ToolTip(status, "Right Ctrl").ShouldBe("Murmur — switching to Whisper small (English)…");
    }

    [Theory]
    [InlineData(EngineState.Ready, false, true)]
    [InlineData(EngineState.Loading, true, true)] // a switch: the previous model still dictates
    [InlineData(EngineState.Loading, false, false)]
    [InlineData(EngineState.NotStarted, false, false)]
    [InlineData(EngineState.Downloading, false, false)]
    [InlineData(EngineState.Error, false, false)]
    public void ShowsReadyIcon_FollowsWhetherTheEngineCanDictate(EngineState state, bool switching, bool expected)
    {
        TrayStatusText.ShowsReadyIcon(new EngineStatus(state, Model, Switching: switching)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(380, "Murmur — downloading speech model 38%")]
    [InlineData(389, "Murmur — downloading speech model 38%")] // whole percent reached, never rounded up
    [InlineData(290, "Murmur — downloading speech model 29%")] // 0.29 * 100 is 28.999… in floating point
    [InlineData(999, "Murmur — downloading speech model 99%")]
    [InlineData(1000, "Murmur — downloading speech model 100%")]
    public void ToolTip_Downloading_ShowsWholePercent(long received, string expected)
    {
        TrayStatusText.ToolTip(Downloading(received, retries: 0), "Right Ctrl").ShouldBe(expected);
    }

    [Fact]
    public void ToolTip_DownloadingWithoutDetails_ShowsZeroPercent()
    {
        TrayStatusText.ToolTip(new EngineStatus(EngineState.Downloading, Model), "Right Ctrl")
            .ShouldBe("Murmur — downloading speech model 0%");
    }

    [Fact]
    public void ToolTip_Reconnecting_SaysSo()
    {
        TrayStatusText.ToolTip(Downloading(380, retries: 2), "Right Ctrl")
            .ShouldBe("Murmur — reconnecting to download the speech model…");
    }

    /// <summary>A model switch downloads the next model while the current one keeps working.</summary>
    [Fact]
    public void ToolTip_ReadyWhileDownloadingAnotherModel_NamesTheHotkey()
    {
        var status = new EngineStatus(EngineState.Ready, Model, "Cuda",
            Download: new DownloadProgress("Whisper small (English)", 100, 1000, 1000, 1, Retries: 1));

        TrayStatusText.ToolTip(status, "Right Ctrl").ShouldBe("Murmur — hold Right Ctrl to talk");
    }

    private static EngineStatus Downloading(long received, int retries) =>
        new(EngineState.Downloading, Model, Download: new DownloadProgress(Model, received, 1000, 1000, 1, retries));

    [Theory]
    [InlineData(EngineState.NotStarted)]
    [InlineData(EngineState.Loading)]
    public void ToolTip_NotYetLoaded_SaysLoading(EngineState state)
    {
        TrayStatusText.ToolTip(new EngineStatus(state, Model), "Right Ctrl").ShouldBe("Murmur — loading model…");
    }

    [Fact]
    public void ToolTip_Error_ShowsTheError()
    {
        TrayStatusText.ToolTip(new EngineStatus(EngineState.Error, Model, Error: "Could not load the speech model"), "Right Ctrl")
            .ShouldBe("Murmur — Could not load the speech model");
    }

    [Fact]
    public void ToolTip_LongError_IsCutToFitTheNotificationArea()
    {
        var error = new string('x', 300);

        var text = TrayStatusText.ToolTip(new EngineStatus(EngineState.Error, Model, Error: error), "Right Ctrl");

        text.Length.ShouldBe(TrayStatusText.MaxToolTipLength);
        text.ShouldStartWith("Murmur — xxx");
        text.ShouldEndWith("…");
    }
}
