using System.ComponentModel;
using Wpf.Ui.Controls;

namespace Murmur.UI;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
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
