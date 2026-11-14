using Murmur.Core.Models;
using Murmur.Core.Speech;
using Murmur.UI.Splash;
using Shouldly;

namespace Murmur.UI.Tests.Splash;

public sealed class SplashViewModelTests
{
    [Fact]
    public void NewOne_ShowsTheVersionAndSaysTheModelIsLoading()
    {
        var model = new SplashViewModel("1.1.0");

        model.Version.ShouldBe("1.1.0");
        model.StatusText.ShouldBe("Loading model…");
        model.StatusTone.ShouldBe(EngineStatusTone.Info);
    }

    [Fact]
    public void Apply_Ready_SaysReadyWithTheRuntime()
    {
        var model = new SplashViewModel("1.1.0");

        model.Apply(new EngineStatus(EngineState.Ready, "Whisper large-v3-turbo", "Cuda"));

        model.StatusText.ShouldBe("Ready · CUDA");
        model.StatusTone.ShouldBe(EngineStatusTone.Ready);
    }

    [Fact]
    public void Apply_Downloading_ShowsThePercent()
    {
        var model = new SplashViewModel("1.1.0");
        var download = new DownloadProgress("Whisper large-v3-turbo", 380, 1000, 0, null, 0);

        model.Apply(new EngineStatus(EngineState.Downloading, "Whisper large-v3-turbo", Download: download));

        model.StatusText.ShouldBe("Downloading speech model… 38%");
        model.StatusTone.ShouldBe(EngineStatusTone.Progress);
    }

    [Fact]
    public void Apply_RaisesPropertyChangedForTextAndTone()
    {
        var model = new SplashViewModel("1.1.0");
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Apply(new EngineStatus(EngineState.Error, "Whisper large-v3-turbo", Error: "The disk is full."));

        changed.ShouldContain(nameof(SplashViewModel.StatusText));
        changed.ShouldContain(nameof(SplashViewModel.StatusTone));
        model.StatusText.ShouldBe("The disk is full.");
    }
}
