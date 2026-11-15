using Microsoft.Web.WebView2.Wpf;
using Serilog;

namespace Murmur.WebView;

public class WebViewControl : WebView2, IWebViewInterop
{
    private static readonly ILogger Logger = Log.ForContext<WebViewControl>();

    public void PostWebMessageAsJson(string message)
    {
        // WebView2 is the UI thread's, and events are published from wherever their news happened.
        if (!CheckAccess())
        {
            Dispatcher.BeginInvoke(() => PostWebMessageAsJson(message));
            return;
        }

        if (CoreWebView2 == null)
        {
            Logger.Debug("Message dropped: CoreWebView2 not initialized");
            return;
        }

        try
        {
            CoreWebView2.PostWebMessageAsJson(message);
        }
        catch (ObjectDisposedException)
        {
            Logger.Debug("Message dropped: WebView2 disposed");
        }
    }
}
