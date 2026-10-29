using System.Windows;
using Murmur.Common;
using Murmur.WebView;

namespace Murmur.Interop;

public class ApiEventAggregator(IWebViewInterop webViewInterop)
{
    public virtual void Publish(string eventName, object data)
    {
        var message = new { EventName = eventName, Data = data };
        var json = JsonUtils.Serialize(message);
        IpcLogger.LogEvent(eventName, json);

        if (Application.Current == null) return;
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
                webViewInterop.PostWebMessageAsJson(json));
        }
        else
        {
            webViewInterop.PostWebMessageAsJson(json);
        }
    }
}
