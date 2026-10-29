using System.Globalization;
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
            using var host = new TrayIconHost(engine, settings);
            host.Show();
            host.ToolTipText.ShouldBe("Murmur — loading model…");

            EngineStatus[] changes =
            [
                new(EngineState.Ready, Model, "Cuda"),
                new(EngineState.Error, Model, Error: "boom"),
                new(EngineState.Ready, Model, "Cuda"),
                new(EngineState.Loading, Model),
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
    public void FormatToolTip_Ready_NamesTheHotkey()
    {
        TrayIconHost.FormatToolTip(new EngineStatus(EngineState.Ready, Model, "Cuda"), "Right Ctrl")
            .ShouldBe("Murmur — hold Right Ctrl to talk");
    }

    [Fact]
    public void FormatToolTip_Downloading_ShowsWholePercent()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            TrayIconHost.FormatToolTip(new EngineStatus(EngineState.Downloading, Model, DownloadProgress: 0.45), "Right Ctrl")
                .ShouldBe("Murmur — downloading model 45%");
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
}
