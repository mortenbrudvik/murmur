using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Murmur.Common;
using Serilog;

namespace Murmur.WebView;

/// <summary>What <see cref="WebViewHandler"/> does about a WebView2 process that failed.</summary>
public enum ProcessFailureResponse
{
    /// <summary>WebView2 recovers by itself (a frame, the GPU or a helper process): only logged.</summary>
    None,

    /// <summary>The page's renderer exited or hung: the page is reloaded.</summary>
    Reload,

    /// <summary>
    /// The page cannot be brought back in this control: the browser process exited, or the renderer kept failing.
    /// <see cref="WebViewHandler.Unrecoverable"/> is raised.
    /// </summary>
    GiveUp,
}

public class WebViewHandler
{
    /// <summary>A renderer that has failed this many times is not reloaded again: it would likely only fail again.</summary>
    public const int MaxReloads = 3;

    private readonly ILogger _logger = Log.ForContext<WebViewHandler>();
    private int _reloads;

    public WebViewHandler(WebView2 webview, string initialUrl, string? environmentFolderPath = null)
    {
        WebView = webview;
        InitialUrl = initialUrl;
        EnvironmentFolderPath = environmentFolderPath;

        WebView.Loaded += (_, _) => InitializeAsync().FireAndForget();
    }

    public Action? InitializeComplete { get; set; }
    public Action<Exception>? InitializeFailed { get; set; }

    /// <summary>
    /// Raised on the UI thread when a WebView2 process failure leaves the page blank for good (see
    /// <see cref="ProcessFailureResponse.GiveUp"/>). Only a new WebView2, in practice a restart of the app, brings it back.
    /// </summary>
    public Action? Unrecoverable { get; set; }
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

    /// <summary>
    /// Whether a right-click shows the browser's context menu when DevTools are off. Text fields (the
    /// vocabulary editor, history search, the setup practice box) keep it for Cut/Copy/Paste, and so does
    /// selected text for Copy; everywhere else it would only offer Back, Reload, Save as and Print.
    /// </summary>
    public static bool ShouldShowContextMenu(bool isEditable, bool hasSelection) => isEditable || hasSelection;

    /// <summary>
    /// What to do about a failed process of <paramref name="kind"/>, after <paramref name="reloadsSoFar"/> reloads.
    /// After the browser process exits the control is closed for good. The page's renderer exiting or hanging is
    /// recovered by a reload, up to <see cref="MaxReloads"/> times. Everything else WebView2 recovers by itself.
    /// </summary>
    public static ProcessFailureResponse RespondTo(CoreWebView2ProcessFailedKind kind, int reloadsSoFar) => kind switch
    {
        CoreWebView2ProcessFailedKind.BrowserProcessExited => ProcessFailureResponse.GiveUp,
        CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive =>
            reloadsSoFar < MaxReloads ? ProcessFailureResponse.Reload : ProcessFailureResponse.GiveUp,
        _ => ProcessFailureResponse.None,
    };

    private void ApplySettings(CoreWebView2Settings settings)
    {
        // DevTools and browser shortcuts (F12, F5/Ctrl+R reload, Ctrl+P, Ctrl+F, zoom keys) follow EnableDevTools,
        // so Debug keeps them and Release and Store builds do not. Editing keys (Ctrl+C/V/X/A/Z) are not affected.
        settings.AreDevToolsEnabled = EnableDevTools;
        settings.AreBrowserAcceleratorKeysEnabled = EnableDevTools;

        // Kept on so text fields have Cut/Copy/Paste; ContextMenuRequested hides it elsewhere in Release.
        settings.AreDefaultContextMenusEnabled = true;

        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        // Debug keeps the error page so a stopped Vite dev server says so instead of showing a blank window.
        settings.IsBuiltInErrorPageEnabled = EnableDevTools;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
    }

    private void OnProcessFailed(CoreWebView2ProcessFailedKind kind, CoreWebView2ProcessFailedReason reason, int exitCode)
    {
        _logger.Error("WebView2 process failed: {Kind} ({Reason}, exit code {ExitCode})", kind, reason, exitCode);
        switch (RespondTo(kind, _reloads))
        {
            case ProcessFailureResponse.Reload:
                _reloads++;
                _logger.Information("Reloading the page ({Reload} of at most {MaxReloads})", _reloads, MaxReloads);
                try
                {
                    WebView.Reload();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not reload the page");
                    Unrecoverable?.Invoke();
                }

                break;
            case ProcessFailureResponse.GiveUp:
                _logger.Error("The page cannot be recovered in this window; a restart brings it back");
                Unrecoverable?.Invoke();
                break;
        }
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

            ApplySettings(WebView.CoreWebView2.Settings);

            if (!EnableDevTools)
            {
                WebView.CoreWebView2.ContextMenuRequested += (_, args) =>
                {
                    var target = args.ContextMenuTarget;
                    if (!ShouldShowContextMenu(target.IsEditable, target.HasSelection))
                        args.Handled = true;
                };
            }

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

            WebView.CoreWebView2.ProcessFailed += (_, args) => OnProcessFailed(args.ProcessFailedKind, args.Reason, args.ExitCode);

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
