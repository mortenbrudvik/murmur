using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
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

    private static void WithPill(Action<Rig> body) =>
        StaHost.Run(() =>
        {
            var vm = new PillViewModel();
            var window = new PillWindow(vm);
            new WindowInteropHelper(window).EnsureHandle();
            try
            {
                body(new Rig(vm, window, new PillPresenter(window, vm)));
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

            rig.Pill.ShowNotice("Copied — press Ctrl+V");
            StaHost.Pump(Short);
            var noticeWidth = rig.Window.ActualWidth;
            CenterX().ShouldBe(centerX, 0.5);

            // The three faces really differ in width, so the checks above mean something.
            transcribingWidth.ShouldBeLessThan(listeningWidth);
            noticeWidth.ShouldBeGreaterThan(listeningWidth);
        });
    }

    [Fact]
    public void ShowNotice_ShowsTheTextThenHidesAfterAboutOneAndAHalfSeconds()
    {
        WithPill(rig =>
        {
            rig.Pill.ShowNotice("Saved to history");
            StaHost.Pump(Short);

            rig.Vm.Mode.ShouldBe(PillMode.Notice);
            rig.Vm.NoticeText.ShouldBe("Saved to history");
            rig.Window.IsVisible.ShouldBeTrue();

            var clock = Stopwatch.StartNew();
            StaHost.PumpUntil(() => !rig.Window.IsVisible, TimeSpan.FromSeconds(4)).ShouldBeTrue();

            clock.Elapsed.ShouldBeInRange(TimeSpan.FromMilliseconds(1200), TimeSpan.FromMilliseconds(2400));
            rig.Vm.Mode.ShouldBe(PillMode.Hidden);
        });
    }

    [Fact]
    public void ShowNotice_CalledAgain_RestartsTheHideTimer()
    {
        WithPill(rig =>
        {
            rig.Pill.ShowNotice("Loading model…");
            StaHost.Pump(TimeSpan.FromSeconds(1));

            rig.Pill.ShowNotice("Loading model…");
            var secondShown = Stopwatch.StartNew();
            StaHost.Pump(TimeSpan.FromSeconds(1)); // 2 s after the first call; its timer would have hidden the pill at 1.5 s

            rig.Window.IsVisible.ShouldBeTrue();

            StaHost.PumpUntil(() => !rig.Window.IsVisible, TimeSpan.FromSeconds(3)).ShouldBeTrue();
            secondShown.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(1300));
        });
    }

    [Theory]
    [InlineData(PillMode.Listening)]
    [InlineData(PillMode.Transcribing)]
    public void Show_AfterNotice_CancelsTheHideTimer(PillMode next)
    {
        WithPill(rig =>
        {
            rig.Pill.ShowNotice("Loading model…");
            if (next == PillMode.Listening)
            {
                rig.Pill.ShowListening();
            }
            else
            {
                rig.Pill.ShowTranscribing();
            }

            StaHost.Pump(TimeSpan.FromMilliseconds(1700));

            rig.Vm.Mode.ShouldBe(next);
            rig.Window.IsVisible.ShouldBeTrue();
        });
    }
}
