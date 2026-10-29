using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Murmur.Interop;
using Murmur.WebView;

namespace Murmur.UI;

public partial class MainViewModel : ObservableObject
{
    private WebViewHandler? _handler;

    [ObservableProperty]
    private WebViewControl _webView;

    public MainViewModel(WebViewControl webView, ApiBridge bridge)
    {
        _webView = webView;

#if DEBUG
        var url = "http://localhost:5223/";
#else
        var url = "https://murmur.localassets.invalid/index.html";
#endif

        var tempPath = Path.Combine(Path.GetTempPath(), "Murmur");

        _handler = new WebViewHandler(WebView, url, tempPath)
        {
            HostObject = bridge,
            HostObjectName = "apibridge",
#if DEBUG
            EnableDevTools = true,
#else
            EnableDevTools = false,
            // BaseDirectory, not CurrentDirectory: the working directory differs for "dotnet run" and autostart.
            HostWebRootFolder = Path.Combine(AppContext.BaseDirectory, "Assets"),
            HostWebHostNameForFolder = "murmur.localassets.invalid",
#endif
        };
    }
}
