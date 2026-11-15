namespace Murmur.WebView;

public interface IWebViewInterop
{
    /// <summary>Sends <paramref name="message"/> to the page. Callable from any thread; dropped while the page is not there.</summary>
    void PostWebMessageAsJson(string message);
}
