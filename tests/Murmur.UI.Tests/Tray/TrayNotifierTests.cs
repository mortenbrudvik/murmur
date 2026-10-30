using Murmur.Core.Models;
using Murmur.UI.Tray;
using Shouldly;

namespace Murmur.UI.Tests.Tray;

public sealed class TrayNotifierTests
{
    private const string Model = "Whisper large-v3-turbo";
    private const string Hotkey = "Right Ctrl";

    private readonly TrayNotifier _notifier = new();

    [Fact]
    public void OnStatus_ReadyAfterADownloadWithTheWindowInactive_AnnouncesReady()
    {
        _notifier.OnStatus(Downloading(retries: 0), windowActive: false, Hotkey).ShouldBeNull();
        _notifier.OnStatus(new EngineStatus(EngineState.Loading, Model), windowActive: false, Hotkey).ShouldBeNull();

        var notice = _notifier.OnStatus(Ready(), windowActive: false, Hotkey);

        notice.ShouldBe(new TrayNotice("Murmur is ready", "Hold Right Ctrl to talk."));
    }

    /// <summary>An ordinary sign-in start loads a model that is already there, and stays silent.</summary>
    [Fact]
    public void OnStatus_ReadyWithoutADownloadThisRun_NoNotice()
    {
        _notifier.OnStatus(new EngineStatus(EngineState.Loading, Model), windowActive: false, Hotkey).ShouldBeNull();

        _notifier.OnStatus(Ready(), windowActive: false, Hotkey).ShouldBeNull();
    }

    [Fact]
    public void OnStatus_ReadyWhileTheWindowIsActive_NoNotice()
    {
        _notifier.OnStatus(Downloading(retries: 0), windowActive: true, Hotkey).ShouldBeNull();

        _notifier.OnStatus(Ready(), windowActive: true, Hotkey).ShouldBeNull();
    }

    /// <summary>The download is used up by the Ready that followed it, whether or not that one notified.</summary>
    [Fact]
    public void OnStatus_ReadyAgainAfterAnActiveWindowSawIt_NoNotice()
    {
        _notifier.OnStatus(Downloading(retries: 0), windowActive: true, Hotkey);
        _notifier.OnStatus(Ready(), windowActive: true, Hotkey);

        _notifier.OnStatus(Ready(), windowActive: false, Hotkey).ShouldBeNull();
    }

    /// <summary>A model switch downloads the next model while the current one is Ready; that is not the end of it.</summary>
    [Fact]
    public void OnStatus_ReadyWhileASwitchDownloadStillRuns_NoNotice()
    {
        _notifier.OnStatus(ReadyWhileSwitching(), windowActive: false, Hotkey).ShouldBeNull();
        _notifier.OnStatus(ReadyWhileSwitching(), windowActive: false, Hotkey).ShouldBeNull();
    }

    [Fact]
    public void OnStatus_ReadyAfterASwitchDownload_AnnouncesAgain()
    {
        _notifier.OnStatus(Downloading(retries: 0), windowActive: false, Hotkey);
        _notifier.OnStatus(Ready(), windowActive: false, Hotkey).ShouldNotBeNull();

        _notifier.OnStatus(ReadyWhileSwitching(), windowActive: false, Hotkey).ShouldBeNull();
        var notice = _notifier.OnStatus(Ready(), windowActive: false, Hotkey);

        notice.ShouldBe(new TrayNotice("Murmur is ready", "Hold Right Ctrl to talk."));
    }

    /// <summary>A switch whose download or load failed stays Ready on the previous model, with the error: nothing became ready.</summary>
    [Fact]
    public void OnStatus_ReadyWithAnErrorAfterASwitchDownload_NoNotice()
    {
        _notifier.OnStatus(ReadyWhileSwitching(), windowActive: false, Hotkey).ShouldBeNull();

        _notifier.OnStatus(Ready() with { Error = "Could not switch to Whisper small (English): 404" }, windowActive: false, Hotkey)
            .ShouldBeNull();
    }

    [Fact]
    public void OnStatus_ReadyWithAnErrorAfterADownload_UsesUpTheDownload()
    {
        _notifier.OnStatus(Downloading(retries: 0), windowActive: false, Hotkey);
        _notifier.OnStatus(Ready() with { Error = "Could not switch to Whisper small (English): 404" }, windowActive: false, Hotkey)
            .ShouldBeNull();

        _notifier.OnStatus(Ready(), windowActive: false, Hotkey).ShouldBeNull();
    }

    [Fact]
    public void OnStatus_ErrorWithTheWindowInactive_SaysSo()
    {
        var notice = _notifier.OnStatus(Failed("Not enough disk space for the speech model."), windowActive: false, Hotkey);

        notice.ShouldBe(new TrayNotice("Murmur needs your attention", "Not enough disk space for the speech model."));
    }

    [Fact]
    public void OnStatus_SecondErrorInTheSameRun_NoNotice()
    {
        _notifier.OnStatus(Failed("first"), windowActive: false, Hotkey).ShouldNotBeNull();

        _notifier.OnStatus(Failed("second"), windowActive: false, Hotkey).ShouldBeNull();
    }

    /// <summary>The user is looking at the window, so the error was not missed and the one notice is still to come.</summary>
    [Fact]
    public void OnStatus_ErrorWhileTheWindowIsActive_NoNoticeAndTheNextOneStillNotifies()
    {
        _notifier.OnStatus(Failed("seen"), windowActive: true, Hotkey).ShouldBeNull();

        _notifier.OnStatus(Failed("missed"), windowActive: false, Hotkey)
            .ShouldBe(new TrayNotice("Murmur needs your attention", "missed"));
    }

    [Fact]
    public void OnStatus_ErrorWithoutAMessage_StillSaysSomething()
    {
        var notice = _notifier.OnStatus(new EngineStatus(EngineState.Error, Model), windowActive: false, Hotkey);

        notice.ShouldNotBeNull().Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void OnStatus_FourthRetry_WarnsOnceAboutTheConnection()
    {
        for (var retries = 0; retries <= 3; retries++)
            _notifier.OnStatus(Downloading(retries), windowActive: false, Hotkey).ShouldBeNull($"{retries} retries");

        var notice = _notifier.OnStatus(Downloading(retries: 4), windowActive: false, Hotkey);

        notice.ShouldBe(new TrayNotice(
            "Murmur can't download its speech model",
            "Check your internet connection. Murmur keeps trying."));
        _notifier.OnStatus(Downloading(retries: 5), windowActive: false, Hotkey).ShouldBeNull();
        _notifier.OnStatus(Downloading(retries: 0), windowActive: false, Hotkey).ShouldBeNull();
        _notifier.OnStatus(Downloading(retries: 4), windowActive: false, Hotkey).ShouldBeNull();
    }

    [Fact]
    public void OnStatus_FourthRetryWhileTheWindowIsActive_NoNoticeAndALaterOneStillNotifies()
    {
        _notifier.OnStatus(Downloading(retries: 4), windowActive: true, Hotkey).ShouldBeNull();

        _notifier.OnStatus(Downloading(retries: 5), windowActive: false, Hotkey).ShouldNotBeNull();
    }

    private static EngineStatus Downloading(int retries) =>
        new(EngineState.Downloading, Model, Download: new DownloadProgress(Model, 380, 1000, 1000, 1, retries));

    private static EngineStatus Ready() => new(EngineState.Ready, Model, "Cuda");

    /// <summary>Ready with the current model while the next one downloads.</summary>
    private static EngineStatus ReadyWhileSwitching() => Ready() with { Download = Downloading(retries: 0).Download };

    private static EngineStatus Failed(string error) =>
        new(EngineState.Error, Model, Error: error);
}
