using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Murmur.Common;
using Murmur.Core.Audio;
using Murmur.Core.Input;
using Murmur.Core.Services;
using Murmur.Interop;
using Murmur.WebView;
using Serilog;

namespace Murmur.UI;

/// <summary>
/// The main window: the web UI it hosts, what happens to running work as the window loses focus, hides or closes, and
/// the native panel shown instead of the web UI when WebView2 fails.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private const string HostObjectName = "apibridge";

    private readonly ILogger _logger = Log.ForContext<MainViewModel>();
    private readonly HotkeyCaptureCoordinator _hotkeyCapture;
    private readonly MicrophoneLevelMonitor _micTest;
    private readonly AppLifecycle _lifecycle;
    private readonly ILinkOpener _links;
    private readonly WebViewHandler _handler;

    [ObservableProperty]
    private WebViewControl _webView;

    /// <summary>WebView2 could not start, or failed for good: the window shows the native failure panel instead.</summary>
    [ObservableProperty]
    private bool _webViewFailed;

    public MainViewModel(
        WebViewControl webView, ApiBridge bridge, HotkeyCaptureCoordinator hotkeyCapture, MicrophoneLevelMonitor micTest,
        AppLifecycle lifecycle, ILinkOpener links, MurmurPaths paths)
    {
        _webView = webView;
        _hotkeyCapture = hotkeyCapture;
        _micTest = micTest;
        _lifecycle = lifecycle;
        _links = links;
        LogsDirectory = paths.LogsDirectory;

#if DEBUG
        const string origin = "http://localhost:5223";
        var url = origin + "/";
#else
        const string origin = "https://murmur.localassets.invalid";
        var url = origin + "/index.html";
#endif

        _handler = new WebViewHandler(WebView, url, paths.WebView2Directory)
        {
            HostObject = bridge,
            HostObjectName = HostObjectName,
            // --bg in src/WebApp/src/index.css: the web area shows it until the page paints, instead of white.
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1c, 0x1f, 0x27),
            // Only the page's own origin. In Debug that is the Vite dev server, whose hot-reload websocket does not
            // raise WebResourceRequested and so needs no entry.
            AllowedOrigins = [origin],
#if DEBUG
            EnableDevTools = true,
#else
            EnableDevTools = false,
            // BaseDirectory, not CurrentDirectory: the working directory differs for "dotnet run" and autostart.
            HostWebRootFolder = Path.Combine(AppContext.BaseDirectory, "Assets"),
            HostWebHostNameForFolder = "murmur.localassets.invalid",
#endif
            // Both arrive on the UI thread. Dictation keeps working from the tray either way.
            InitializeFailed = _ => WebViewFailed = true,
            Unrecoverable = () => WebViewFailed = true,
        };
    }

    /// <summary>Where the logs are, for the failure panel.</summary>
    public string LogsDirectory { get; }

    /// <summary>The microphone test's latest stop for the window, which runs off the UI thread. Tests wait for it.</summary>
    internal Task MicTestStopping { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The window lost focus. A hotkey capture started from Settings ends: once the owner has clicked away, the keys
    /// pressed next are meant for another app.
    /// </summary>
    public void OnWindowDeactivated() => _hotkeyCapture.Cancel();

    /// <summary>
    /// The window was hidden. A hotkey capture ends, and so does a microphone test from the setup page: it must not keep
    /// the microphone open once nobody can see its meter. The page hears that the test ended and starts it again when
    /// the window is back.
    /// </summary>
    public void OnWindowHidden()
    {
        _hotkeyCapture.Cancel();
        StopMicTest();
    }

    /// <summary>The window was minimized: a microphone test ends, as when it is hidden.</summary>
    public void OnWindowMinimized() => StopMicTest();

    /// <summary>
    /// The window is closing. Returns true when it must only hide instead: Murmur keeps running in the tray, and the page
    /// keeps its state. When Murmur is exiting it really closes, and the page loses the bridge first.
    /// </summary>
    public bool OnWindowClosing()
    {
        if (!_lifecycle.IsQuitting)
            return true;

        try
        {
            WebView.CoreWebView2?.RemoveHostObjectFromScript(HostObjectName);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not remove the bridge from the page while closing");
        }

        return false;
    }

    [RelayCommand]
    private void Restart() => _lifecycle.RequestRestart();

    [RelayCommand]
    private void OpenLogFolder() => _links.Open(LogsDirectory);

    [RelayCommand]
    private void Quit() => _lifecycle.RequestQuit();

    /// <summary>
    /// Off the UI thread: ending the test releases the microphone, which waits for a microphone that is still opening
    /// (a Bluetooth headset can take a second).
    /// </summary>
    private void StopMicTest()
    {
        var stopping = Task.Run(_micTest.StopForWindow);
        MicTestStopping = stopping;
        stopping.FireAndForget();
    }
}
