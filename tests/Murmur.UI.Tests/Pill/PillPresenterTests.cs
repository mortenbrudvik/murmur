using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Murmur.Core.Dictation;
using Murmur.UI.Pill;
using Shouldly;

namespace Murmur.UI.Tests.Pill;

/// <summary>
/// Drives a real <see cref="PillWindow"/> on an STA thread. These tests briefly show a real (never activated)
/// window on the desktop, so they need an interactive desktop session.
/// </summary>
public class PillPresenterTests
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOPMOST = 0x8;

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    private sealed record Rig(PillViewModel Vm, PillWindow Window, PillPresenter Presenter)
    {
        public IPillPresenter Pill => Presenter;
    }

    /// <summary>Short notice times for tests that wait for a notice to go, so they do not wait out the real ones.</summary>
    private static readonly TimeSpan TestInfo = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan TestProblem = TimeSpan.FromMilliseconds(900);

    private static void WithPill(Action<Rig> body) => WithPill(realTimes: true, workArea: null, body);

    private static void WithPill(bool realTimes, Func<(Rect WorkAreaDip, double Dpi)>? workArea, Action<Rig> body) =>
        StaHost.Run(() =>
        {
            var vm = new PillViewModel();
            var window = new PillWindow(vm);
            new WindowInteropHelper(window).EnsureHandle();
            var presenter = realTimes && workArea is null
                ? new PillPresenter(window, vm)
                : new PillPresenter(
                    window, vm, realTimes ? PillPresenter.DefaultInfoDuration : TestInfo,
                    realTimes ? PillPresenter.DefaultProblemDuration : TestProblem, workArea);
            try
            {
                body(new Rig(vm, window, presenter));
            }
            finally
            {
                window.Close();
            }
        });

    // Calls the presenter the way the dictation controller does: from a thread that is not the UI thread.
    private static void OnAnotherThread(Action action) => Task.Run(action).Wait();

    [Fact]
    public void Window_AfterHandleCreated_IsNoActivateToolWindowClickThroughAndTopmost()
    {
        WithPill(rig =>
        {
            var style = GetWindowLong(new WindowInteropHelper(rig.Window).Handle, GWL_EXSTYLE);

            (style & WS_EX_NOACTIVATE).ShouldBe(WS_EX_NOACTIVATE);
            (style & WS_EX_TOOLWINDOW).ShouldBe(WS_EX_TOOLWINDOW);
            (style & WS_EX_TRANSPARENT).ShouldBe(WS_EX_TRANSPARENT);
            (style & WS_EX_TOPMOST).ShouldBe(WS_EX_TOPMOST);
        });
    }

    [Fact]
    public void ShowListening_FromAnotherThread_ShowsThePillWithoutTakingFocus()
    {
        WithPill(rig =>
        {
            var foreground = GetForegroundWindow();

            OnAnotherThread(rig.Pill.ShowListening);
            StaHost.PumpUntil(() => rig.Window.IsVisible, TimeSpan.FromSeconds(2)).ShouldBeTrue();
            StaHost.Pump(Short);

            rig.Vm.Mode.ShouldBe(PillMode.Listening);
            rig.Window.IsActive.ShouldBeFalse();
            GetForegroundWindow().ShouldBe(foreground);
        });
    }

    [Fact]
    public void ShowListening_FirstShow_SizesTheWindowToTheCapsulePlusShadowMargin()
    {
        WithPill(rig =>
        {
            rig.Pill.ShowListening();
            StaHost.Pump(Short);

            var capsule = rig.Window.MeasureCapsule();
            rig.Window.ActualWidth.ShouldBe(capsule.Width + PillWindow.ShadowMargin.Left + PillWindow.ShadowMargin.Right, 0.01);
            rig.Window.ActualHeight.ShouldBe(capsule.Height + PillWindow.ShadowMargin.Top + PillWindow.ShadowMargin.Bottom, 0.01);
        });
    }

    [Fact]
    public void Hide_AfterShow_HidesTheWindow()
    {
        WithPill(rig =>
        {
            rig.Pill.ShowListening();
            StaHost.Pump(Short);
            rig.Window.IsVisible.ShouldBeTrue();

            OnAnotherThread(rig.Pill.Hide);
            StaHost.Pump(Short);

            rig.Window.IsVisible.ShouldBeFalse();
            rig.Vm.Mode.ShouldBe(PillMode.Hidden);
        });
    }

    [Fact]
    public void Hide_BeforeAnyShow_DoesNotBreakTheFirstShow()
    {
        WithPill(rig =>
        {
            rig.Pill.Hide();
            rig.Pill.ShowListening();
            StaHost.Pump(TimeSpan.FromMilliseconds(300));

            rig.Window.IsVisible.ShouldBeTrue();
            IsWindowVisible(new WindowInteropHelper(rig.Window).Handle).ShouldBeTrue();
        });
    }

    [Fact]
    public void Calls_FromAnotherThread_AreAppliedInOrder()
    {
        WithPill(rig =>
        {
            OnAnotherThread(() =>
            {
                rig.Pill.ShowListening();
                rig.Pill.PushLevel(-20f);
                rig.Pill.ShowTranscribing();
                rig.Pill.Hide();
                rig.Pill.ShowListening();
                rig.Pill.PushLevel(-40f);
            });
            StaHost.Pump(Short);

            rig.Vm.Mode.ShouldBe(PillMode.Listening);
            rig.Window.IsVisible.ShouldBeTrue();
            rig.Vm.Bars[^1].ShouldBe(0.6, 0.001);
            rig.Vm.Bars[^2].ShouldBe(0.2, 0.001); // the -20 dBFS push was before this recording started
        });
    }

    [Fact]
    public void ModeSwitch_WhileVisible_KeepsThePillCentered()
    {
        WithPill(rig =>
        {
            double CenterX() => rig.Window.Left + rig.Window.ActualWidth / 2;

            rig.Pill.ShowListening();
            StaHost.Pump(Short);
            var centerX = CenterX();
            var listeningWidth = rig.Window.ActualWidth;

            rig.Pill.ShowTranscribing();
            StaHost.Pump(Short);
            var transcribingWidth = rig.Window.ActualWidth;
            CenterX().ShouldBe(centerX, 0.5);

            rig.Pill.ShowNotice("Copied — press Ctrl+V", NoticeKind.Problem);
            StaHost.Pump(Short);
            var noticeWidth = rig.Window.ActualWidth;
            CenterX().ShouldBe(centerX, 0.5);

            // The three faces really differ in width, so the checks above mean something.
            transcribingWidth.ShouldBeLessThan(listeningWidth);
            noticeWidth.ShouldBeGreaterThan(listeningWidth);
        });
    }

    [Theory]
    [InlineData(NoticeKind.Info, 1.5)]
    [InlineData(NoticeKind.Problem, 4.5)]
    public void ShowNotice_ShowsTheTextForTheTimeOfItsKind(NoticeKind kind, double seconds)
    {
        WithPill(rig =>
        {
            rig.Pill.ShowNotice("Saved to history", kind);
            StaHost.Pump(Short);

            rig.Vm.Mode.ShouldBe(PillMode.Notice);
            rig.Vm.NoticeText.ShouldBe("Saved to history");
            rig.Window.IsVisible.ShouldBeTrue();
            rig.Presenter.NoticeDuration.ShouldBe(TimeSpan.FromSeconds(seconds));
        });
    }

    [Fact]
    public void ShowNotice_TimeUp_HidesThePill()
    {
        WithPill(realTimes: false, workArea: null, rig =>
        {
            rig.Pill.ShowNotice("Saved to history", NoticeKind.Info);
            StaHost.Pump(Short);
            rig.Window.IsVisible.ShouldBeTrue();

            StaHost.PumpUntil(() => !rig.Window.IsVisible, TimeSpan.FromSeconds(5)).ShouldBeTrue();
            rig.Vm.Mode.ShouldBe(PillMode.Hidden);
            rig.Presenter.NoticeDuration.ShouldBeNull();
        });
    }

    [Fact]
    public void ShowNotice_InfoAfterAProblem_UsesTheShorterTime()
    {
        WithPill(rig =>
        {
            rig.Pill.ShowNotice("Copied — press Ctrl+V", NoticeKind.Problem);
            rig.Pill.ShowNotice("Loading model…", NoticeKind.Info);
            StaHost.Pump(Short);

            rig.Presenter.NoticeDuration.ShouldBe(PillPresenter.DefaultInfoDuration);
        });
    }

    [Fact]
    public void ShowNotice_CalledAgain_RestartsTheHideTimer()
    {
        WithPill(realTimes: false, workArea: null, rig =>
        {
            // The problem notice stays 900 ms. Shown again after 600 ms, it must still be there 600 ms later, 1.2 s
            // after the first call, when the first call's timer would have hidden it. 300 ms either side of each limit.
            rig.Pill.ShowNotice("Copied — press Ctrl+V", NoticeKind.Problem);
            StaHost.Pump(TimeSpan.FromMilliseconds(600));

            rig.Pill.ShowNotice("Copied — press Ctrl+V", NoticeKind.Problem);
            StaHost.Pump(TimeSpan.FromMilliseconds(600));

            rig.Window.IsVisible.ShouldBeTrue();
            StaHost.PumpUntil(() => !rig.Window.IsVisible, TimeSpan.FromSeconds(5)).ShouldBeTrue();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShowTranscribing_DotsPulseOnlyWithWindowsAnimationEffectsOn(bool animationEffects)
    {
        WithPill(rig =>
        {
            // The real value comes from Windows (SystemParameters.ClientAreaAnimation); set here so both cases run.
            var dots = rig.Window.TranscribingDots;
            dots.Tag = animationEffects;

            rig.Pill.ShowTranscribing();
            StaHost.Pump(Short);

            var dot = (UIElement)dots.Template.FindName("Dot1", dots);
            dot.HasAnimatedProperties.ShouldBe(animationEffects);
            if (!animationEffects)
                dot.Opacity.ShouldBe(0.6);
        });
    }

    [Fact]
    public void ShowStarting_AfterANotice_MovesToTheForegroundMonitor()
    {
        var workArea = new Rect(0, 0, 800, 600);
        var dpi = 96.0;
        WithPill(realTimes: true, workArea: () => (workArea, dpi), rig =>
        {
            double CenterX() => rig.Window.Left + rig.Window.ActualWidth / 2;

            // The pill's own DPI, so that the placement in pixels needs no rescaling by Windows.
            dpi = VisualTreeHelper.GetDpi(rig.Window).PixelsPerInchX;

            rig.Pill.ShowNotice("Copied — press Ctrl+V", NoticeKind.Problem);
            StaHost.Pump(Short);
            CenterX().ShouldBe(400, 1);

            // The next press is in a window on another part of the desktop, while the notice still shows.
            workArea = new Rect(400, 0, 800, 600);
            rig.Pill.ShowStarting();
            StaHost.Pump(Short);
            CenterX().ShouldBe(800, 1);

            // Within the dictation the pill stays put, wherever the foreground window goes.
            workArea = new Rect(0, 0, 800, 600);
            rig.Pill.ShowListening();
            rig.Pill.ShowTranscribing();
            StaHost.Pump(Short);
            CenterX().ShouldBe(800, 1);
        });
    }

    [Fact]
    public void ShowStarting_FromAnotherThread_ShowsThePillWithoutTakingFocus()
    {
        WithPill(rig =>
        {
            var foreground = GetForegroundWindow();

            OnAnotherThread(rig.Pill.ShowStarting);
            StaHost.PumpUntil(() => rig.Window.IsVisible, TimeSpan.FromSeconds(2)).ShouldBeTrue();
            StaHost.Pump(Short);

            rig.Vm.Mode.ShouldBe(PillMode.Starting);
            rig.Window.IsActive.ShouldBeFalse();
            GetForegroundWindow().ShouldBe(foreground);
        });
    }

    // Red means recording: while the microphone starts, the pill shows a hollow dot in its place.
    [Fact]
    public void ShowStarting_ThenListening_ShowsTheRecordingDotOnlyOnceListening()
    {
        WithPill(rig =>
        {
            var recordingDot = (FrameworkElement)rig.Window.FindName("RecordingDot");
            var startingDot = (FrameworkElement)rig.Window.FindName("StartingDot");

            rig.Pill.ShowStarting();
            StaHost.Pump(Short);
            recordingDot.IsVisible.ShouldBeFalse();
            startingDot.IsVisible.ShouldBeTrue();

            rig.Pill.ShowListening();
            StaHost.Pump(Short);
            recordingDot.IsVisible.ShouldBeTrue();
            startingDot.IsVisible.ShouldBeFalse();
        });
    }

    [Fact]
    public void ShowStarting_ThenListening_KeepsThePillTheSameSizeAndPlace()
    {
        WithPill(rig =>
        {
            rig.Pill.ShowStarting();
            StaHost.Pump(Short);
            var (left, width) = (rig.Window.Left, rig.Window.ActualWidth);

            rig.Pill.ShowListening();
            StaHost.Pump(Short);

            rig.Window.ActualWidth.ShouldBe(width, 0.01);
            rig.Window.Left.ShouldBe(left, 0.5); // within a pixel: Left reads back rounded to device pixels after a move
            width.ShouldBeGreaterThan(PillWindow.ShadowMargin.Left + PillWindow.ShadowMargin.Right + 40); // the dot and bars, not an empty capsule
        });
    }

    [Theory]
    [InlineData(PillMode.Starting)]
    [InlineData(PillMode.Listening)]
    [InlineData(PillMode.Transcribing)]
    public void Show_AfterNotice_CancelsTheHideTimer(PillMode next)
    {
        WithPill(rig =>
        {
            rig.Pill.ShowNotice("Loading model…", NoticeKind.Info);
            if (next == PillMode.Starting)
            {
                rig.Pill.ShowStarting();
            }
            else if (next == PillMode.Listening)
            {
                rig.Pill.ShowListening();
            }
            else
            {
                rig.Pill.ShowTranscribing();
            }

            StaHost.Pump(Short);

            rig.Vm.Mode.ShouldBe(next);
            rig.Window.IsVisible.ShouldBeTrue();
            rig.Presenter.NoticeDuration.ShouldBeNull(); // no timer left to hide the dictation's pill
        });
    }
}
