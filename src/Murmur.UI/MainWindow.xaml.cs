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
        var webView = (DataContext as MainViewModel)?.WebView;
        webView?.CoreWebView2?.RemoveHostObjectFromScript("apibridge");

        base.OnClosing(e);
    }
}
