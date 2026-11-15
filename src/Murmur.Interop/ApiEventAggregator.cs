using Murmur.Common;
using Murmur.WebView;

namespace Murmur.Interop;

/// <summary>Pushes an event to the page, from any thread: <see cref="IWebViewInterop"/> takes it to the UI thread.</summary>
public class ApiEventAggregator(IWebViewInterop webViewInterop)
{
    public virtual void Publish(string eventName, object data)
    {
        var message = new { EventName = eventName, Data = data };
        var json = JsonUtils.Serialize(message);
        IpcLogger.LogEvent(eventName, json);
        webViewInterop.PostWebMessageAsJson(json);
    }
}
