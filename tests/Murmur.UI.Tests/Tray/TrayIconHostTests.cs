using System.Globalization;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.UI.Tray;
using NSubstitute;
using Shouldly;

namespace Murmur.UI.Tests.Tray;

public sealed class TrayIconHostTests
{
    private const string Model = "Whisper large-v3-turbo";

    /// <summary>
    /// Puts a real icon in the notification area for a moment. The icon must survive switching between ready and
    /// not ready in both directions (the TaskbarIcon disposes each icon it is given once it is replaced).
    /// </summary>
    [Fact]
    public void StatusChanges_FollowedByIconAndToolTip_InBothDirections()
    {
        var engine = Substitute.For<ISpeechEngine>();
        var status = new EngineStatus(EngineState.Loading, Model);
        engine.Status.Returns(_ => status);
        var settings = Substitute.For<ISettingsService>();
        settings.Load().Returns(MurmurSettings.Default with { Hotkey = new HotkeyDefinition(0xA5, HotkeyModifiers.None, "Right Alt") });

        StaHost.Run(() =>
        {
            using var host = new TrayIconHost(engine, settings, new InputStatus());
            host.Show();
            host.ToolTipText.ShouldBe("Murmur — loading model…");

            EngineStatus[] changes =
            [
                new(EngineState.Ready, Model, "Cuda"),
                new(EngineState.Error, Model, Error: "boom"),
                new(EngineState.Ready, Model, "Cuda"),
                new(EngineState.Loading, Model),
                new(EngineState.Loading, "Whisper small (English)", Switching: true),
                new(EngineState.Ready, Model, "Cuda"),
            ];
            foreach (var change in changes)
            {
                status = change;
                engine.StatusChanged += Raise.Event<Action<EngineStatus>>(change);
                StaHost.PumpUntil(() => host.ToolTipText == TrayIconHost.FormatToolTip(change, "Right Alt"), TimeSpan.FromSeconds(2))
                    .ShouldBeTrue($"tooltip for {change.State}");
            }
        });
    }

    [Fact]
    public void Show_TaskbarNotReady_DoesNotThrowAndRetriesUntilTheIconIsAdded()
    {
        var attempts = 0;
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(_ =>
            {
                if (++attempts < 3)
                    throw new InvalidOperationException("TryCreate failed.");
            }, maxCreateAttempts: 10);

            Should.NotThrow(host.Show);

            StaHost.PumpUntil(() => attempts == 3, TimeSpan.FromSeconds(5)).ShouldBeTrue();
            StaHost.Pump(TimeSpan.FromMilliseconds(200));
            attempts.ShouldBe(3); // no more attempts once the icon is in
        });
    }

    [Fact]
    public void Show_TaskbarNeverReady_StopsRetryingAfterTheLastAttempt()
    {
        var attempts = 0;
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(_ =>
            {
                attempts++;
                throw new InvalidOperationException("TryCreate failed.");
            }, maxCreateAttempts: 4);

            Should.NotThrow(host.Show);

            StaHost.PumpUntil(() => attempts == 4, TimeSpan.FromSeconds(5)).ShouldBeTrue();
            StaHost.Pump(TimeSpan.FromMilliseconds(200));
            attempts.ShouldBe(4);
        });
    }

    [Fact]
    public void Dispose_WhileRetrying_StopsRetrying()
    {
        var attempts = 0;
        StaHost.Run(() =>
        {
            var host = NewHostWithCreator(_ =>
            {
                attempts++;
                throw new InvalidOperationException("TryCreate failed.");
            }, maxCreateAttempts: 10);
            host.Show();

            host.Dispose();
            StaHost.Pump(TimeSpan.FromMilliseconds(200));

            attempts.ShouldBe(1);
        });
    }

    [Fact]
    public void ShowNotification_BeforeShow_DoesNotThrow()
    {
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(_ => { }, maxCreateAttempts: 1);

            Should.NotThrow(() => host.ShowNotification("Murmur is ready", "Hold Right Ctrl to talk."));
        });
    }

    [Fact]
    public void ShowNotification_AfterDispose_DoesNothing()
    {
        StaHost.Run(() =>
        {
            var host = NewHostWithCreator(_ => { }, maxCreateAttempts: 1);
            host.Show();
            host.Dispose();

            Should.NotThrow(() => host.ShowNotification("Murmur is ready", "Hold Right Ctrl to talk."));
        });
    }

    /// <summary>Right after sign-in the taskbar may not be ready; the notice waits for the icon instead of being lost.</summary>
    [Fact]
    public void ShowNotification_IconNotAddedYet_IsShownOnceTheIconIsAdded()
    {
        var shown = new List<(string Title, string Message)>();
        var attempts = 0;
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(
                _ =>
                {
                    if (++attempts < 3)
                        throw new InvalidOperationException("TryCreate failed.");
                },
                maxCreateAttempts: 10,
                show: (_, title, message) =>
                {
                    shown.Add((title, message));
                    return true;
                });
            host.Show();

            host.ShowNotification("Murmur can't use the hotkey", "Restart Murmur to try again.");

            shown.ShouldBeEmpty();
            StaHost.PumpUntil(() => shown.Count > 0, TimeSpan.FromSeconds(5)).ShouldBeTrue();
            StaHost.Pump(TimeSpan.FromMilliseconds(200));
            shown.ShouldBe([("Murmur can't use the hotkey", "Restart Murmur to try again.")]);
            attempts.ShouldBe(3);
        });
    }

    /// <summary>Only the newest waits: a notice that is overtaken is stale by the time the icon is there.</summary>
    [Fact]
    public void ShowNotification_SeveralBeforeTheIconIsAdded_ShowsOnlyTheLatest()
    {
        var shown = new List<string>();
        var attempts = 0;
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(
                _ =>
                {
                    if (++attempts < 2)
                        throw new InvalidOperationException("TryCreate failed.");
                },
                maxCreateAttempts: 10,
                show: (_, title, _) =>
                {
                    shown.Add(title);
                    return true;
                });
            host.Show();

            host.ShowNotification("first", "one");
            host.ShowNotification("second", "two");

            StaHost.PumpUntil(() => shown.Count > 0, TimeSpan.FromSeconds(5)).ShouldBeTrue();
            StaHost.Pump(TimeSpan.FromMilliseconds(200));
            shown.ShouldBe(["second"]);
        });
    }

    [Fact]
    public void ShowNotification_IconAlreadyAdded_ShowsAtOnceAndOnlyOnce()
    {
        var shown = new List<string>();
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(
                _ => { },
                maxCreateAttempts: 1,
                show: (_, title, _) =>
                {
                    shown.Add(title);
                    return true;
                });
            host.Show();

            host.ShowNotification("Murmur is ready", "Hold Right Ctrl to talk.");
            StaHost.Pump(TimeSpan.FromMilliseconds(100));

            shown.ShouldBe(["Murmur is ready"]);
        });
    }

    [Fact]
    public void ShowNotification_IconNeverAdded_ShowsNothingAndDoesNotThrow()
    {
        var shown = 0;
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(
                _ => throw new InvalidOperationException("TryCreate failed."),
                maxCreateAttempts: 1,
                show: (_, _, _) =>
                {
                    shown++;
                    return true;
                });
            host.Show();

            Should.NotThrow(() => host.ShowNotification("Murmur needs your attention", "boom"));
            StaHost.Pump(TimeSpan.FromMilliseconds(200));

            shown.ShouldBe(0);
        });
    }

    [Fact]
    public void ShowNotification_DisposedBeforeTheIconIsAdded_DropsTheNotice()
    {
        var shown = 0;
        var attempts = 0;
        StaHost.Run(() =>
        {
            var host = NewHostWithCreator(
                _ =>
                {
                    if (++attempts < 2)
                        throw new InvalidOperationException("TryCreate failed.");
                },
                maxCreateAttempts: 10,
                show: (_, _, _) =>
                {
                    shown++;
                    return true;
                });
            host.Show();
            host.ShowNotification("Murmur needs your attention", "boom");

            host.Dispose();
            StaHost.Pump(TimeSpan.FromMilliseconds(200));

            shown.ShouldBe(0);
        });
    }

    /// <summary>Showing a balloon is not worth a crash, and a failure is not retried: the notice would only repeat.</summary>
    [Fact]
    public void ShowNotification_ShowingFails_DoesNotThrowAndIsNotKept()
    {
        var attempts = 0;
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(
                _ => { },
                maxCreateAttempts: 1,
                show: (_, _, _) =>
                {
                    attempts++;
                    throw new InvalidOperationException("Shell_NotifyIcon failed.");
                });
            host.Show();

            Should.NotThrow(() => host.ShowNotification("Murmur needs your attention", "boom"));
            StaHost.Pump(TimeSpan.FromMilliseconds(100));

            attempts.ShouldBe(1);
        });
    }

    [Fact]
    public void BalloonClick_AsksToOpenTheWindow()
    {
        StaHost.Run(() =>
        {
            H.NotifyIcon.TaskbarIcon? icon = null;
            using var host = NewHostWithCreator(created => icon = created, maxCreateAttempts: 1);
            var opened = 0;
            host.OpenRequested += () => opened++;
            host.Show();

            icon.ShouldNotBeNull().RaiseEvent(new System.Windows.RoutedEventArgs(H.NotifyIcon.TaskbarIcon.TrayBalloonTipClickedEvent));

            opened.ShouldBe(1);
        });
    }

    [Fact]
    public void FormatToolTip_Ready_NamesTheHotkey()
    {
        TrayIconHost.FormatToolTip(new EngineStatus(EngineState.Ready, Model, "Cuda"), "Right Ctrl")
            .ShouldBe("Murmur — hold Right Ctrl to talk");
    }

    [Fact]
    public void FormatToolTip_HookUnavailable_SaysTheHotkeyIsOff()
    {
        TrayIconHost.FormatToolTip(new EngineStatus(EngineState.Ready, Model, "Cuda"), "Right Ctrl", hookInstalled: false)
            .ShouldBe("Murmur — the hotkey isn't working");
    }

    /// <summary>Only the line that advertises the hotkey changes; what the engine is doing is still the news.</summary>
    [Fact]
    public void FormatToolTip_HookUnavailableWhileTheEngineLoads_StillSaysLoading()
    {
        TrayIconHost.FormatToolTip(new EngineStatus(EngineState.Loading, Model), "Right Ctrl", hookInstalled: false)
            .ShouldBe("Murmur — loading model…");
    }

    /// <summary>The old model keeps dictating while the next one loads, so this is not "loading model…".</summary>
    [Fact]
    public void FormatToolTip_SwitchingModels_NamesTheModelBeingLoaded()
    {
        var status = new EngineStatus(EngineState.Loading, "Whisper small (English)", "Cuda", Switching: true);

        TrayIconHost.FormatToolTip(status, "Right Ctrl").ShouldBe("Murmur — switching to Whisper small (English)…");
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
        TrayIconHost.ShowsReadyIcon(new EngineStatus(state, Model, Switching: switching)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(380, "Murmur — downloading speech model 38%")]
    [InlineData(389, "Murmur — downloading speech model 38%")] // whole percent reached, never rounded up
    [InlineData(290, "Murmur — downloading speech model 29%")] // 0.29 * 100 is 28.999… in floating point
    [InlineData(999, "Murmur — downloading speech model 99%")]
    [InlineData(1000, "Murmur — downloading speech model 100%")]
    public void FormatToolTip_Downloading_ShowsWholePercent(long received, string expected)
    {
        InEnglish(() => TrayIconHost.FormatToolTip(Downloading(received, retries: 0), "Right Ctrl").ShouldBe(expected));
    }

    [Fact]
    public void FormatToolTip_DownloadingWithoutDetails_ShowsZeroPercent()
    {
        InEnglish(() => TrayIconHost.FormatToolTip(new EngineStatus(EngineState.Downloading, Model), "Right Ctrl")
            .ShouldBe("Murmur — downloading speech model 0%"));
    }

    [Fact]
    public void FormatToolTip_Reconnecting_SaysSo()
    {
        TrayIconHost.FormatToolTip(Downloading(380, retries: 2), "Right Ctrl")
            .ShouldBe("Murmur — reconnecting to download the speech model…");
    }

    /// <summary>A model switch downloads the next model while the current one keeps working.</summary>
    [Fact]
    public void FormatToolTip_ReadyWhileDownloadingAnotherModel_NamesTheHotkey()
    {
        var status = new EngineStatus(EngineState.Ready, Model, "Cuda",
            Download: new DownloadProgress("Whisper small (English)", 100, 1000, 1000, 1, Retries: 1));

        TrayIconHost.FormatToolTip(status, "Right Ctrl").ShouldBe("Murmur — hold Right Ctrl to talk");
    }

    private static EngineStatus Downloading(long received, int retries) =>
        new(EngineState.Downloading, Model, Download: new DownloadProgress(Model, received, 1000, 1000, 1, retries));

    private static void InEnglish(Action assert)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            assert();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(EngineState.NotStarted)]
    [InlineData(EngineState.Loading)]
    public void FormatToolTip_NotYetLoaded_SaysLoading(EngineState state)
    {
        TrayIconHost.FormatToolTip(new EngineStatus(state, Model), "Right Ctrl").ShouldBe("Murmur — loading model…");
    }

    [Fact]
    public void FormatToolTip_Error_ShowsTheError()
    {
        TrayIconHost.FormatToolTip(new EngineStatus(EngineState.Error, Model, Error: "Could not load the speech model"), "Right Ctrl")
            .ShouldBe("Murmur — Could not load the speech model");
    }

    [Fact]
    public void FormatToolTip_LongError_IsCutToFitTheNotificationArea()
    {
        var error = new string('x', 300);

        var text = TrayIconHost.FormatToolTip(new EngineStatus(EngineState.Error, Model, Error: error), "Right Ctrl");

        text.Length.ShouldBe(TrayIconHost.MaxToolTipLength);
        text.ShouldStartWith("Murmur — xxx");
        text.ShouldEndWith("…");
    }

    private static TrayIconHost NewHostWithCreator(
        Action<H.NotifyIcon.TaskbarIcon> create, int maxCreateAttempts, Func<H.NotifyIcon.TaskbarIcon, string, string, bool>? show = null)
    {
        var engine = Substitute.For<ISpeechEngine>();
        engine.Status.Returns(new EngineStatus(EngineState.Loading, Model));
        // Without a show seam a notice would be a real balloon, which a test must never put on the owner's screen.
        show ??= static (_, _, _) => true;
        return new TrayIconHost(engine, Substitute.For<ISettingsService>(), new InputStatus(), create, TimeSpan.FromMilliseconds(20), maxCreateAttempts, show);
    }
}
