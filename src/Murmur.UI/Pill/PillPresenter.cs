using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Murmur.Core.Dictation;
using Serilog;

namespace Murmur.UI.Pill;

/// <summary>
/// Drives the pill from any thread. Every call is queued to the window's dispatcher in call order; the window is
/// never activated, so the app being dictated into keeps focus.
/// </summary>
public sealed class PillPresenter(PillWindow window, PillViewModel vm) : IPillPresenter
{
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(1.5);

    // Send runs each call ahead of lower-priority work already queued (hotkey signals, recorder chunks, rendering), but it
    // cannot interrupt work that is running: the pill shows only once the UI thread is free (so the microphone opens off
    // it). One priority for every call keeps them in order.
    private const DispatcherPriority CallPriority = DispatcherPriority.Send;

    private readonly ILogger _logger = Log.ForContext<PillPresenter>();

    private DispatcherTimer? _noticeTimer;
    private bool _shownBefore;

    // Work area (DIPs) of the monitor the pill was last shown on; reused while it stays visible.
    private Rect _workAreaDip;

    public void ShowListening() => Post(() =>
    {
        StopNoticeTimer();
        Show(PillMode.Listening);
    });

    public void PushLevel(float rmsDbfs) => Post(() => vm.PushLevel(rmsDbfs));

    public void ShowTranscribing() => Post(() =>
    {
        StopNoticeTimer();
        Show(PillMode.Transcribing);
    });

    public void ShowNotice(string text) => Post(() =>
    {
        vm.NoticeText = text;
        Show(PillMode.Notice);
        RestartNoticeTimer();
    });

    public void Hide() => Post(() =>
    {
        StopNoticeTimer();
        HideNow();
    });

    private void Post(Action action) =>
        window.Dispatcher.BeginInvoke(CallPriority, new Action(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // The pill is cosmetic: a failure here must never break dictation.
                _logger.Error(ex, "Pill update failed");
            }
        }));

    private void Show(PillMode mode)
    {
        var wasHidden = !window.IsVisible;
        vm.Mode = mode;

        if (wasHidden)
        {
            _workAreaDip = GetForegroundWorkAreaDip();
        }

        // Also when already visible: the capsule changes width between modes and must stay centered.
        // The monitor is only chosen when the pill appears.
        Reposition();

        if (wasHidden)
        {
            var stopwatch = _shownBefore ? null : Stopwatch.StartNew();
            _shownBefore = true;

            // ShowActivated is false and the window is WS_EX_NOACTIVATE, so WPF shows it with SW_SHOWNOACTIVATE.
            // Calling ShowWindow directly would leave WPF thinking the window is collapsed, and it would never lay out or render.
            window.Show();

            // Observed: the first Show of a window whose handle already existed sizes it about 25 DIPs wider than its
            // content, which would stretch the capsule off-center. A fresh layout pass corrects it.
            window.InvalidateMeasure();
            window.UpdateLayout();

            if (stopwatch is not null)
                _logger.Debug("First pill Show took {Milliseconds:F1} ms", stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private void HideNow()
    {
        // Nothing to hide before the first Show, and a never-shown window is best left untouched.
        if (window.IsVisible)
        {
            window.Hide();
        }

        vm.Mode = PillMode.Hidden;
    }

    private void Reposition()
    {
        // Resize the window now, so that it is never seen at its new position with its old size.
        window.UpdateLayout();

        var topLeft = PillPlacement.Compute(_workAreaDip, window.MeasureCapsule());
        window.Left = topLeft.X - PillWindow.ShadowMargin.Left;
        window.Top = topLeft.Y - PillWindow.ShadowMargin.Top;
    }

    /// <summary>The work area of the monitor showing the foreground window, in DIPs at that monitor's DPI.</summary>
    private static Rect GetForegroundWorkAreaDip()
    {
        if (!PillNative.TryGetForegroundWorkArea(out var workArea, out var dpi))
        {
            return SystemParameters.WorkArea;
        }

        var scale = 96.0 / dpi;
        return new Rect(
            workArea.left * scale,
            workArea.top * scale,
            (workArea.right - workArea.left) * scale,
            (workArea.bottom - workArea.top) * scale);
    }

    private void RestartNoticeTimer()
    {
        _noticeTimer ??= CreateNoticeTimer();
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private void StopNoticeTimer() => _noticeTimer?.Stop();

    private DispatcherTimer CreateNoticeTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = NoticeDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            HideNow();
        };
        return timer;
    }
}
