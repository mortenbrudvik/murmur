using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Murmur.Common;
using Serilog;

namespace Murmur.WebView;

public class WebViewHandler
{
    private readonly ILogger _logger = Log.ForContext<WebViewHandler>();

    public WebViewHandler(WebView2 webview, string initialUrl, string? environmentFolderPath = null)
    {
        WebView = webview;
        InitialUrl = initialUrl;
        EnvironmentFolderPath = environmentFolderPath;

        WebView.Loaded += (_, _) => InitializeAsync().FireAndForget();
    }

    public Action? InitializeComplete { get; set; }
    public Action<Exception>? InitializeFailed { get; set; }
    public WebView2 WebView { get; private set; }
    public string InitialUrl { get; }
    public string? EnvironmentFolderPath { get; private set; }
    public bool IsInitialized { get; private set; }
    public string HostObjectName { get; init; } = "webview";
    public object? HostObject { get; init; }
    public bool EnableDevTools { get; init; }
    public string? HostWebRootFolder { get; init; }
    public string HostWebHostNameForFolder { get; init; } = "murmur.localassets.invalid";
    public HashSet<string> AllowedHosts { get; init; } = ["localhost", "murmur.localassets.invalid"];

    public virtual void Navigate(string url, bool forceRefresh = false)
    {
        if (string.IsNullOrEmpty(url) || !IsInitialized) return;
        WebView.Source = new Uri(url);
    }

    protected async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: EnvironmentFolderPath,
                options: new CoreWebView2EnvironmentOptions
                {
                    AllowSingleSignOnUsingOSPrimaryAccount = false
                });
            await WebView.EnsureCoreWebView2Async(environment);

            if (HostObject != null)
            {
                WebView.CoreWebView2.AddHostObjectToScript(HostObjectName, HostObject);
            }

            if (EnableDevTools)
                WebView.CoreWebView2.OpenDevToolsWindow();

            if (!string.IsNullOrEmpty(HostWebRootFolder))
            {
                // Allow used here because the React app fetches assets cross-origin from the virtual host
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    HostWebHostNameForFolder,
                    HostWebRootFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            WebView.CoreWebView2.ProcessFailed += (sender, args) =>
            {
                _logger.Error("WebView2 process failed: {Kind}", args.ProcessFailedKind);
            };

            WebView.CoreWebView2.NavigationCompleted += (sender, args) =>
            {
                if (args.IsSuccess)
                    _logger.Debug("Navigation completed successfully");
                else
                    _logger.Warning("Navigation failed: {WebErrorStatus}", args.WebErrorStatus);
            };

            WebView.CoreWebView2.NavigationStarting += (sender, args) =>
            {
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) &&
                    !AllowedHosts.Contains(uri.Host))
                {
                    _logger.Warning("Blocked navigation to {Uri}", args.Uri);
                    args.Cancel = true;
                }
            };

            WebView.CoreWebView2.FrameNavigationStarting += (sender, args) =>
            {
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) &&
                    !AllowedHosts.Contains(uri.Host))
                {
                    _logger.Warning("Blocked frame navigation to {Uri}", args.Uri);
                    args.Cancel = true;
                }
            };

            WebView.CoreWebView2.NewWindowRequested += (sender, args) =>
            {
                _logger.Warning("Blocked popup request to {Uri}", args.Uri);
                args.Handled = true;
            };

            IsInitialized = true;
            InitializeComplete?.Invoke();

            if (!string.IsNullOrEmpty(InitialUrl))
                Navigate(InitialUrl);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "WebView2 initialization failed");
            InitializeFailed?.Invoke(ex);
        }
    }
}
