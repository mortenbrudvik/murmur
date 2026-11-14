using System.ComponentModel;
using Murmur.Core.Audio;
using Murmur.Core.Input;
using Wpf.Ui.Controls;

namespace Murmur.UI;

public partial class MainWindow : FluentWindow
{
    private bool _cloaked;

    public MainWindow(MainViewModel viewModel, HotkeyCaptureCoordinator hotkeyCapture, MicrophoneLevelMonitor micTest)
    {
        DataContext = viewModel;
        InitializeComponent();

        // A hotkey capture started from the settings page must not outlive the window's focus: once the owner has
        // clicked away (or the window hid), the keys pressed next are meant for another app. Likewise a microphone
        // test from the setup page must not keep the microphone open once nobody can see its meter: hidden or
        // minimized. The page hears that the test ended and starts it again when the window is back.
        Deactivated += (_, _) => hotkeyCapture.Cancel();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                hotkeyCapture.Cancel();
                micTest.StopForWindow();
            }
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == System.Windows.WindowState.Minimized)
                micTest.StopForWindow();
        };
    }

    /// <summary>
    /// Shows the window where nobody can see it, so that its page loads while the splash is up. WebView2 only starts
    /// in a window that has really been shown (measured: a handle and a layout pass are not enough), so the window is
    /// shown cloaked: Windows and WPF treat it as visible and the page loads and paints, but the compositor does not
    /// draw it. It does get its taskbar button, like any app that is starting. <see cref="Reveal"/> uncloaks it.
    /// If cloaking is refused the window is simply visible from here on, underneath the splash.
    /// </summary>
    public void Preload()
    {
        if (IsVisible)
            return;

        var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        _cloaked = WindowNative.SetCloaked(handle, true);
        ShowActivated = false;
        Show();
    }

    /// <summary>
    /// Makes the window visible to the user, whether it was preloaded, hidden or never shown, and restores it if it
    /// was minimized. It takes the focus only when <paramref name="activate"/> says so: after the splash, a user who
    /// has gone back to another app is left there.
    /// </summary>
    public void Reveal(bool activate)
    {
        // Read by Show for a window that is not visible yet; a preloaded one is already shown and ignores it.
        ShowActivated = activate;
        Show();
        if (WindowState == System.Windows.WindowState.Minimized)
            WindowState = System.Windows.WindowState.Normal;

        if (_cloaked)
        {
            WindowNative.SetCloaked(new System.Windows.Interop.WindowInteropHelper(this).Handle, false);
            _cloaked = false;
        }

        if (activate)
            Activate();
    }

    /// <summary>True between <see cref="Preload"/> and <see cref="Reveal"/>: shown, but not drawn.</summary>
    public bool IsPreloaded => _cloaked;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (App.IsQuitting)
        {
            var webView = (DataContext as MainViewModel)?.WebView;
            webView?.CoreWebView2?.RemoveHostObjectFromScript("apibridge");
        }
        else
        {
            // Closing only hides the window: Murmur keeps running in the tray, and the page keeps its state.
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
