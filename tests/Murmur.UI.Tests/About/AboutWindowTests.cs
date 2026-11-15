using System.Windows.Input;
using System.Windows.Interop;
using MediatR;
using Murmur.Core.Insertion;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.UI.About;
using NSubstitute;
using Shouldly;

namespace Murmur.UI.Tests.About;

/// <summary>The About window is never shown here; its handle is enough to raise input on it and to close it.</summary>
public sealed class AboutWindowTests
{
    [Fact]
    public void Escape_ClosesTheWindow()
    {
        StaHost.Run(() =>
        {
            var window = new AboutWindow(NewModel());
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
            var closed = 0;
            window.Closed += (_, _) => closed++;

            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });

            closed.ShouldBe(1);
        });
    }

    [Fact]
    public void CloseCommand_ClosesTheWindow()
    {
        StaHost.Run(() =>
        {
            var model = NewModel();
            var window = new AboutWindow(model);
            new WindowInteropHelper(window).EnsureHandle();
            var closed = 0;
            window.Closed += (_, _) => closed++;

            model.CloseCommand.Execute(null);

            closed.ShouldBe(1);
        });
    }

    /// <summary>The view model outlives each window: a closed window must not hear the next one being closed.</summary>
    [Fact]
    public void CloseCommand_AfterAnEarlierWindowWasClosed_ClosesOnlyTheNewOne()
    {
        StaHost.Run(() =>
        {
            var model = NewModel();
            var first = new AboutWindow(model);
            new WindowInteropHelper(first).EnsureHandle();
            first.Close();
            var firstClosedAgain = 0;
            first.Closed += (_, _) => firstClosedAgain++;

            var second = new AboutWindow(model);
            new WindowInteropHelper(second).EnsureHandle();
            var closed = 0;
            second.Closed += (_, _) => closed++;

            Should.NotThrow(() => model.CloseCommand.Execute(null));

            closed.ShouldBe(1);
            firstClosedAgain.ShouldBe(0);
        });
    }

    private static AboutViewModel NewModel()
    {
        var app = Substitute.For<IAppInfo>();
        app.Version.Returns("1.2.0");
        app.Channel.Returns("Built from source");
        app.DataFolder.Returns(@"C:\Users\me\AppData\Local\Murmur");
        app.WindowsBuild.Returns("Windows 11, build 26200");
        app.BuildYear.Returns(2026);
        var history = Substitute.For<IHistoryStore>();
        history.Totals().Returns(new HistoryTotals(0, 0, null));
        var engine = Substitute.For<ISpeechEngine>();
        engine.Status.Returns(new EngineStatus(EngineState.Loading, "Whisper large-v3-turbo"));

        return new AboutViewModel(app, history, engine, Substitute.For<IMediator>(), Substitute.For<ITextInserter>(), TimeProvider.System);
    }
}
