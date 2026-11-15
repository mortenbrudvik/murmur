using System.IO;
using MediatR;
using Murmur.Core.Audio;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Interop;
using Murmur.WebView;
using NSubstitute;
using Shouldly;

namespace Murmur.UI.Tests;

/// <summary>The main window's rules, driven through the view model the way the window's events reach it.</summary>
public class MainViewModelTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private readonly IAudioRecorder _micRecorder = Substitute.For<IAudioRecorder>();
    private readonly HotkeyCaptureCoordinator _hotkeyCapture = new(TimeProvider.System, Substitute.For<IKeyboardLayout>());
    private readonly AppLifecycle _lifecycle;
    private readonly ILinkOpener _links = Substitute.For<ILinkOpener>();
    private readonly MurmurPaths _paths = new(Path.Combine(Path.GetTempPath(), "murmur-vm-tests"));
    private readonly MicrophoneLevelMonitor _micTest;

    // What starting the new Murmur on a restart does; never a real process here.
    private Action _startNewInstance = () => { };

    public MainViewModelTests()
    {
        _lifecycle = new AppLifecycle(() => _startNewInstance());
        _micTest = new MicrophoneLevelMonitor(() => _micRecorder, Substitute.For<IMediator>(), TimeProvider.System);
    }

    private void WithViewModel(Action<MainViewModel> body) =>
        StaHost.Run(() =>
        {
            var vm = new MainViewModel(
                new WebViewControl(), new ApiBridge(Substitute.For<IMediator>(), new ApiRegistry()), _hotkeyCapture, _micTest,
                _lifecycle, _links, _paths);
            body(vm);
        });

    [Fact]
    public void OnWindowDeactivated_EndsAHotkeyCapture()
    {
        WithViewModel(vm =>
        {
            var capture = _hotkeyCapture.CaptureAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

            vm.OnWindowDeactivated();

            capture.Wait(WaitLimit).ShouldBeTrue();
            capture.Result.ShouldBeNull();
        });
    }

    [Fact]
    public void OnWindowHidden_EndsTheCaptureAndStopsTheMicTestOffTheUiThread()
    {
        var cancelThread = -1;
        _micRecorder.When(r => r.Cancel()).Do(_ => cancelThread = Environment.CurrentManagedThreadId);
        _micTest.Start(null).State.ShouldBe(MicTestState.Listening);

        WithViewModel(vm =>
        {
            var capture = _hotkeyCapture.CaptureAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

            vm.OnWindowHidden();

            capture.Wait(WaitLimit).ShouldBeTrue();
            vm.MicTestStopping.Wait(WaitLimit).ShouldBeTrue();
            _micRecorder.Received(1).Cancel();
            cancelThread.ShouldNotBe(Environment.CurrentManagedThreadId);
        });
    }

    [Fact]
    public void OnWindowMinimized_StopsTheMicTest()
    {
        _micTest.Start(null).State.ShouldBe(MicTestState.Listening);

        WithViewModel(vm =>
        {
            vm.OnWindowMinimized();

            vm.MicTestStopping.Wait(WaitLimit).ShouldBeTrue();
            _micRecorder.Received(1).Cancel();
        });
    }

    [Fact]
    public void OnWindowClosing_WhileRunning_OnlyHides()
    {
        WithViewModel(vm => vm.OnWindowClosing().ShouldBeTrue());
    }

    [Fact]
    public void OnWindowClosing_WhileQuitting_ReallyCloses()
    {
        _lifecycle.MarkQuitting();

        WithViewModel(vm => vm.OnWindowClosing().ShouldBeFalse());
    }

    [Fact]
    public void FailurePanelCommands_RestartQuitAndOpenTheLogFolder()
    {
        var restarts = 0;
        var quits = 0;
        _lifecycle.RestartRequested += () => restarts++;
        _lifecycle.QuitRequested += () => quits++;

        WithViewModel(vm =>
        {
            vm.WebViewFailed.ShouldBeFalse();

            vm.RestartCommand.Execute(null);
            vm.QuitCommand.Execute(null);
            vm.OpenLogFolderCommand.Execute(null);
        });

        restarts.ShouldBe(1);
        quits.ShouldBe(1);
        _links.Received(1).Open(_paths.LogsDirectory);
    }

    /// <summary>The failure panel's buttons are commands: a failure must leave the app running, not crash it.</summary>
    [Fact]
    public void FailurePanelCommands_RestartAndOpenLogFolderFail_DoNotThrow()
    {
        var restarts = 0;
        _lifecycle.RestartRequested += () => restarts++;
        _startNewInstance = () => throw new System.ComponentModel.Win32Exception(5);
        _links.When(l => l.Open(Arg.Any<string>())).Throw(new System.ComponentModel.Win32Exception(2));

        WithViewModel(vm =>
        {
            Should.NotThrow(() => vm.RestartCommand.Execute(null));
            Should.NotThrow(() => vm.OpenLogFolderCommand.Execute(null));
        });

        restarts.ShouldBe(0);
        _lifecycle.IsQuitting.ShouldBeFalse();
    }
}
