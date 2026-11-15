using System.Runtime.InteropServices;
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

    /// <summary>The host the bundled page is served from (<see cref="HostWebRootFolder"/>) unless another is given.</summary>
    public const string DefaultHostName = "murmur.localassets.invalid";

    private readonly ILogger _logger = Log.ForContext<WebViewHandler>();
    private int _reloads;

    public WebViewHandler(WebView2 webview, string initialUrl, string? environmentFolderPath = null)
    {
        WebView = webview;
        InitialUrl = initialUrl;
        EnvironmentFolderPath = environmentFolderPath;

        WebView.Loaded += (_, _) => StartAsync().FireAndForget();
    }

    private Task? _start;

    /// <summary>
    /// Starts WebView2 and loads the page, once: later calls return the same task. The control's Loaded event calls
    /// it, and Loaded is raised again whenever the control rejoins a shown window, which must not set WebView2 up a
    /// second time. Call on the UI thread.
    /// </summary>
    public Task StartAsync() => _start ??= InitializeAsync();

    public Action<Exception>? InitializeFailed { get; set; }

    /// <summary>
    /// Raised on the UI thread when a WebView2 process failure leaves the page blank for good (see
    /// <see cref="ProcessFailureResponse.GiveUp"/>). Only a new WebView2, in practice a restart of the app, brings it back.
    /// </summary>
    public Action? Unrecoverable { get; set; }
    public WebView2 WebView { get; }
    public string InitialUrl { get; }
    public string? EnvironmentFolderPath { get; }
    public string HostObjectName { get; init; } = "webview";
    public object? HostObject { get; init; }
    public bool EnableDevTools { get; init; }
    public string? HostWebRootFolder { get; init; }
    public string HostWebHostNameForFolder { get; init; } = DefaultHostName;

    /// <summary>
    /// The origins (scheme, host and port, such as <c>https://murmur.localassets.invalid</c>) the page may navigate to
    /// and fetch from. Everything else is cancelled or answered with 403, so no other page receives the host object.
    /// Empty by default: nothing loads until the origin the app is served from is listed.
    /// </summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>
    /// The colour the web area shows before the page paints, instead of white. Read once, as WebView2 is initialized.
    /// </summary>
    public System.Drawing.Color? DefaultBackgroundColor { get; init; }

    /// <summary>
    /// Whether a right-click shows the browser's context menu when DevTools are off. Text fields (the
    /// vocabulary editor, history search, the setup practice box) keep it for Cut/Copy/Paste, and so does
    /// selected text for Copy; everywhere else it would only offer Back, Reload, Save as and Print.
    /// </summary>
    public static bool ShouldShowContextMenu(bool isEditable, bool hasSelection) => isEditable || hasSelection;

    /// <summary>
    /// Whether <paramref name="uri"/> has the scheme, host and port of one of <paramref name="allowedOrigins"/>. A URI
    /// that does not parse, or has no host (<c>about:blank</c>, <c>data:</c>, <c>file:</c>), is not allowed.
    /// </summary>
    public static bool IsAllowedOrigin(string? uri, IEnumerable<string> allowedOrigins)
    {
        var origin = OriginOf(uri);
        return origin != null && allowedOrigins.Any(allowed => OriginOf(allowed) == origin);
    }

    /// <summary>The scheme, host and port of <paramref name="uri"/>, lower case and with the port always written out.</summary>
    private static string? OriginOf(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host) || parsed.Port < 0)
            return null;
        return $"{parsed.Scheme}://{parsed.IdnHost}:{parsed.Port}".ToLowerInvariant();
    }

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
        // A touchpad swipe would go back to the about:blank the control starts on; NavigationStarting cancels that, but
        // there is no history worth swiping through.
        settings.IsSwipeNavigationEnabled = false;
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

    private async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: EnvironmentFolderPath,
                options: new CoreWebView2EnvironmentOptions
                {
                    AllowSingleSignOnUsingOSPrimaryAccount = false
                });

            await WebView.EnsureCoreWebView2Async(environment, ControllerOptions(environment));
            var core = WebView.CoreWebView2;

            ApplySettings(core.Settings);

            if (!EnableDevTools)
            {
                core.ContextMenuRequested += (_, args) =>
                {
                    var target = args.ContextMenuTarget;
                    if (!ShouldShowContextMenu(target.IsEditable, target.HasSelection))
                        args.Handled = true;
                };
            }

            if (HostObject != null)
                core.AddHostObjectToScript(HostObjectName, HostObject);

            if (EnableDevTools)
                core.OpenDevToolsWindow();

            if (!string.IsNullOrEmpty(HostWebRootFolder))
            {
                // The page is served from this host, and its own requests are same-origin, which CORS does not govern.
                // DenyCors only stops pages from other origins (which the filters below block anyway) reading the files.
                core.SetVirtualHostNameToFolderMapping(
                    HostWebHostNameForFolder,
                    HostWebRootFolder,
                    CoreWebView2HostResourceAccessKind.DenyCors);
            }

            InstallRequestFilter(core);
            InstallNavigationGuards(core);
            core.ProcessFailed += (_, args) => OnProcessFailed(args.ProcessFailedKind, args.Reason, args.ExitCode);

            if (!string.IsNullOrEmpty(InitialUrl))
                WebView.Source = new Uri(InitialUrl);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "WebView2 initialization failed");
            InitializeFailed?.Invoke(ex);
        }
    }

    /// <summary>
    /// The background colour, given to the browser as it is created so not even its first frame is white, and kept on
    /// the control so the control does not put its own default back. A runtime too old for the option still gets the
    /// control's colour, a moment later. Null when there is no colour to give, or the runtime cannot take it.
    /// </summary>
    private CoreWebView2ControllerOptions? ControllerOptions(CoreWebView2Environment environment)
    {
        if (DefaultBackgroundColor is not { } background)
            return null;

        WebView.DefaultBackgroundColor = background;
        try
        {
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.DefaultBackgroundColor = background;
            return options;
        }
        catch (Exception ex) when (ex is NotImplementedException or InvalidCastException or COMException)
        {
            _logger.Debug(ex, "This WebView2 runtime cannot take the background colour at creation");
            return null;
        }
    }

    /// <summary>
    /// Every request from the page, its frames and workers: anything outside the allowed origins gets a 403 without
    /// reaching the network. The bundled CSP already blocks such requests in Release; this also covers Debug, which has
    /// no CSP, and anything the CSP does not govern.
    /// </summary>
    private void InstallRequestFilter(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (IsAllowedOrigin(args.Request.Uri, AllowedOrigins))
                return;
            _logger.Warning("Blocked request to {Uri}", args.Request.Uri);
            args.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
        };
    }

    /// <summary>
    /// The page and its frames navigate only within the allowed origins, and never open a window. The initial
    /// about:blank is navigated while the control is created, before these handlers exist.
    /// </summary>
    private void InstallNavigationGuards(CoreWebView2 core)
    {
        core.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
                _logger.Debug("Navigation completed successfully");
            else
                _logger.Warning("Navigation failed: {WebErrorStatus}", args.WebErrorStatus);
        };

        core.NavigationStarting += (_, args) =>
        {
            if (!IsAllowedOrigin(args.Uri, AllowedOrigins))
            {
                _logger.Warning("Blocked navigation to {Uri}", args.Uri);
                args.Cancel = true;
            }
        };

        core.FrameNavigationStarting += (_, args) =>
        {
            if (!IsAllowedOrigin(args.Uri, AllowedOrigins))
            {
                _logger.Warning("Blocked frame navigation to {Uri}", args.Uri);
                args.Cancel = true;
            }
        };

        core.NewWindowRequested += (_, args) =>
        {
            _logger.Warning("Blocked popup request to {Uri}", args.Uri);
            args.Handled = true;
        };
    }
}
