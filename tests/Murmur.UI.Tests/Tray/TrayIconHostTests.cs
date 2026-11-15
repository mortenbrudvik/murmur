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
                StaHost.PumpUntil(() => host.ToolTipText == TrayStatusText.ToolTip(change, "Right Alt"), TimeSpan.FromSeconds(2))
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

    /// <summary>
    /// After the retries run out, the icon still appears when Explorer announces the taskbar: H.NotifyIcon adds it, not a
    /// retry. A waiting notice is then shown at the next update, and notices after that at once instead of waiting forever.
    /// </summary>
    [Fact]
    public void ShowNotification_IconAppearsWithoutARetryAddingIt_IsShownOnTheNextUpdate()
    {
        var shown = new List<string>();
        var exists = false;
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(
                _ => throw new InvalidOperationException("TryCreate failed."),
                maxCreateAttempts: 1,
                show: (_, title, _) => shown.Add(title),
                isCreated: _ => exists);
            host.Show();
            host.ShowNotification("Murmur can't use the hotkey", "Restart Murmur to try again.", persistent: true);
            shown.ShouldBeEmpty();

            exists = true;
            host.Refresh();

            shown.ShouldBe(["Murmur can't use the hotkey"]);

            host.ShowNotification("Murmur is ready", "Hold Right Ctrl to talk.");

            shown.ShouldBe(["Murmur can't use the hotkey", "Murmur is ready"]);
        });
    }

    [Fact]
    public void ShowNotification_ShownAtOnce_DropsAnOlderWaitingOne()
    {
        var shown = new List<string>();
        var exists = false;
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(
                _ => throw new InvalidOperationException("TryCreate failed."),
                maxCreateAttempts: 1,
                show: (_, title, _) => shown.Add(title),
                isCreated: _ => exists);
            host.Show();
            host.ShowNotification("old", "one");

            exists = true; // the icon is there, but nothing has looked yet
            host.ShowNotification("new", "two");
            host.Refresh();

            shown.ShouldBe(["new"]);
        });
    }

    /// <summary>The hotkey stays off for the whole run, so a later notice does not push its notice out while the icon is missing.</summary>
    [Fact]
    public void ShowNotification_ALastingNoticeIsWaiting_IsShownFirstAndNotDisplacedByALaterOne()
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
                show: (_, title, _) => shown.Add(title));
            host.Show();

            host.ShowNotification("Murmur can't use the hotkey", "Restart Murmur to try again.", persistent: true);
            host.ShowNotification("Murmur needs your attention", "boom");

            StaHost.PumpUntil(() => shown.Count > 0, TimeSpan.FromSeconds(5)).ShouldBeTrue();
            StaHost.Pump(TimeSpan.FromMilliseconds(200));
            shown.ShouldBe(["Murmur can't use the hotkey"]);
        });
    }

    /// <summary>Only a notice that is still waiting is protected: once it was shown, the notices after it are ordinary again.</summary>
    [Fact]
    public void ShowNotification_ALastingNoticeWasShown_LaterOnesAreShownToo()
    {
        var shown = new List<string>();
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(_ => { }, maxCreateAttempts: 1, show: (_, title, _) => shown.Add(title));
            host.Show();

            host.ShowNotification("Murmur can't use the hotkey", "Restart Murmur to try again.", persistent: true);
            host.ShowNotification("Murmur needs your attention", "boom");

            shown.ShouldBe(["Murmur can't use the hotkey", "Murmur needs your attention"]);
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
    public void Show_Menu_HasSettingsAboutSeparatorQuit_InThatOrder()
    {
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(_ => { }, maxCreateAttempts: 1);
            host.Show();

            var items = host.Menu.ShouldNotBeNull().Items;

            items.Count.ShouldBe(4);
            ((System.Windows.Controls.MenuItem)items[0]).Header.ShouldBe("Settings");
            ((System.Windows.Controls.MenuItem)items[1]).Header.ShouldBe("About");
            items[2].ShouldBeOfType<System.Windows.Controls.Separator>();
            ((System.Windows.Controls.MenuItem)items[3]).Header.ShouldBe("Quit");
        });
    }

    [Fact]
    public void Settings_Clicked_RaisesSettingsRequestedAndNothingElse()
    {
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(_ => { }, maxCreateAttempts: 1);
            var settings = 0;
            var others = 0;
            host.SettingsRequested += () => settings++;
            host.OpenRequested += () => others++;
            host.AboutRequested += () => others++;
            host.QuitRequested += () => others++;
            host.Show();

            var item = (System.Windows.Controls.MenuItem)host.Menu.ShouldNotBeNull().Items[0];
            item.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));

            settings.ShouldBe(1);
            others.ShouldBe(0);
        });
    }

    [Fact]
    public void About_Clicked_RaisesAboutRequestedAndNothingElse()
    {
        StaHost.Run(() =>
        {
            using var host = NewHostWithCreator(_ => { }, maxCreateAttempts: 1);
            var about = 0;
            var others = 0;
            host.AboutRequested += () => about++;
            host.OpenRequested += () => others++;
            host.SettingsRequested += () => others++;
            host.QuitRequested += () => others++;
            host.Show();

            var item = (System.Windows.Controls.MenuItem)host.Menu.ShouldNotBeNull().Items[1];
            item.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));

            about.ShouldBe(1);
            others.ShouldBe(0);
        });
    }

    /// <summary>
    /// The menu is moved next to the icon as it opens (TrayMenuPlacement). An opening that cannot be placed, here a
    /// menu with no window, must stay a menu: nothing thrown.
    /// </summary>
    [Fact]
    public void MenuOpened_NothingToPlace_DoesNotThrow()
    {
        StaHost.Run(() =>
        {
            H.NotifyIcon.TaskbarIcon? icon = null;
            using var host = NewHostWithCreator(created => icon = created, maxCreateAttempts: 1);
            host.Show();

            Should.NotThrow(() => icon.ShouldNotBeNull().RaiseEvent(
                new System.Windows.RoutedEventArgs(H.NotifyIcon.TaskbarIcon.TrayContextMenuOpenEvent)));
            StaHost.Pump(TimeSpan.FromMilliseconds(50));
        });
    }

    /// <summary>
    /// Windows asks for the menu in the same way for a right click as for Shift+F10 on the focused icon. A right click
    /// is answered by H.NotifyIcon, at the click; answering it as a keyboard request as well moved the menu to the
    /// corner of the screen, away from the icon (seen on the owner's PC).
    /// </summary>
    [Fact]
    public void KeyboardContextMenu_RightAfterARightClick_IsLeftToTheClick()
    {
        StaHost.Run(() =>
        {
            H.NotifyIcon.TaskbarIcon? icon = null;
            using var host = NewHostWithCreator(created => icon = created, maxCreateAttempts: 1);
            host.Show();

            icon.ShouldNotBeNull().RaiseEvent(new System.Windows.RoutedEventArgs(H.NotifyIcon.TaskbarIcon.TrayRightMouseDownEvent));
            icon.RaiseEvent(new System.Windows.RoutedEventArgs(H.NotifyIcon.TaskbarIcon.TrayKeyboardContextMenuEvent));

            host.Menu.ShouldNotBeNull().IsOpen.ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData(null, 5_000L, false)] // the mouse has not touched the icon
    [InlineData(4_900L, 5_000L, true)]
    [InlineData(4_001L, 5_000L, true)]
    [InlineData(4_000L, 5_000L, false)] // a second ago: that click is over
    [InlineData(1_000L, 5_000L, false)]
    public void IsFromMouse_OnlyRightAfterTheRightButtonWasUsedOnTheIcon(long? lastRightMouseAt, long now, bool expected)
    {
        TrayIconHost.IsFromMouse(lastRightMouseAt, now).ShouldBe(expected);
    }

    private static TrayIconHost NewHostWithCreator(
        Action<H.NotifyIcon.TaskbarIcon> create, int maxCreateAttempts, Action<H.NotifyIcon.TaskbarIcon, string, string>? show = null,
        Func<H.NotifyIcon.TaskbarIcon, bool>? isCreated = null)
    {
        var engine = Substitute.For<ISpeechEngine>();
        engine.Status.Returns(new EngineStatus(EngineState.Loading, Model));
        // Without a show seam a notice would be a real balloon, which a test must never put on the owner's screen.
        show ??= static (_, _, _) => { };
        return new TrayIconHost(engine, Substitute.For<ISettingsService>(), new InputStatus(), create, TimeSpan.FromMilliseconds(20), maxCreateAttempts, show, isCreated);
    }
}
