using System.ComponentModel;
using Murmur.Core.Input;
using Wpf.Ui.Controls;

namespace Murmur.UI;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel, HotkeyCaptureCoordinator hotkeyCapture)
    {
        DataContext = viewModel;
        InitializeComponent();

        // A hotkey capture started from the settings page must not outlive the window's focus: once the owner has
        // clicked away (or the window hid), the keys pressed next are meant for another app.
        Deactivated += (_, _) => hotkeyCapture.Cancel();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
                hotkeyCapture.Cancel();
        };
    }

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
