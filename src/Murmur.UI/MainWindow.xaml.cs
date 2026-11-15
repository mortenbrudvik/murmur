using System.ComponentModel;
using System.Windows;
using Wpf.Ui.Controls;

namespace Murmur.UI;

/// <summary>Hosts the web UI. Window events are forwarded to <see cref="MainViewModel"/>, which decides what they mean.</summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private bool _cloaked;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Deactivated += (_, _) => _viewModel.OnWindowDeactivated();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
                _viewModel.OnWindowHidden();
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                _viewModel.OnWindowMinimized();
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
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        if (_cloaked)
        {
            WindowNative.SetCloaked(new System.Windows.Interop.WindowInteropHelper(this).Handle, false);
            _cloaked = false;
        }

        if (activate)
            Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.OnWindowClosing())
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
