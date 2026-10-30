using System.IO;
using System.Windows;
using System.Windows.Interop;
using Autofac;
using MediatR;
using Murmur.Common;
using Murmur.Core.Dictation;
using Murmur.Core.Features.Engine;
using Murmur.Core.Features.History;
using Murmur.Core.Features.Settings;
using Murmur.Core.Features.Vocabulary;
using Murmur.Core.Input;
using Murmur.Core.Insertion;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.Interop;
using Murmur.UI.Pill;
using Murmur.UI.Tray;
using Serilog;
using Wpf.Ui.Appearance;

namespace Murmur.UI;

public partial class App : Application
{
    private const string MinimizedArgument = "--minimized";

    private readonly CancellationTokenSource _appCts = new();

    private IContainer? _container;
    private SingleInstanceGuard? _guard;
    private MainWindow? _mainWindow;
    private InputHookHost? _inputHooks;
    private TrayIconHost? _tray;

    /// <summary>Set once Murmur is really exiting; until then closing the main window only hides it.</summary>
    public static bool IsQuitting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Start(e.Args);
        }
        catch (Exception ex)
        {
            // OnStartup runs as a dispatcher operation, so an exception escaping it would be swallowed by the
            // DispatcherUnhandledException handler and leave a process with no window, perhaps no tray icon, and the
            // single-instance mutex held, so that every later launch would only signal it and exit.
            FailStartup(ex);
        }
    }

    private void Start(string[] args)
    {
        // 1. Logging
        ConfigureLogging();
        Log.Information("Murmur starting");

        // 2. WebView2
        if (!IsWebView2RuntimeInstalled())
        {
            MessageBox.Show(
                "WebView2 runtime is required. Please install it from https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                "Missing Dependency",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "UI thread exception");
            args.Handled = args.Exception is not (OutOfMemoryException or StackOverflowException);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Log.Fatal(ex, "Unhandled exception");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        // 3. Single instance: a second launch shows the running instance's window and exits.
        _guard = new SingleInstanceGuard();
        if (!_guard.TryAcquire())
        {
            _guard.SignalExisting();
            Shutdown();
            return;
        }

        // 4. Murmur lives in the tray: windows opening and closing never end it, only Quit does.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 5. Container and API
        _container = AutofacBootstrapper.Bootstrap();

        var registry = _container.Resolve<ApiRegistry>();
        registry.Register<GetHistoryRequest>("history", "get");
        registry.Register<DeleteHistoryEntryRequest>("history", "delete");
        registry.Register<ClearHistoryRequest>("history", "clear");
        registry.Register<GetVocabularyRequest>("vocabulary", "get");
        registry.Register<UpdateVocabularyRequest>("vocabulary", "update");
        registry.Register<GetSettingsRequest>("settings", "get");
        registry.Register<UpdateSettingsRequest>("settings", "update");
        registry.Register<CaptureHotkeyRequest>("settings", "captureHotkey");
        registry.Register<GetMicrophonesRequest>("settings", "getMicrophones");
        registry.Register<GetEngineStatusRequest>("engine", "getStatus");

        // 6. The theme is fixed to dark and applied before any window is created to avoid a flicker.
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        // Created before the pill and set explicitly: WPF makes the first window it sees the MainWindow, and that
        // must never be the pill. Its WebView only starts when the window is first shown.
        _mainWindow = _container.Resolve<MainWindow>();
        MainWindow = _mainWindow;

        // 7. The pill's window handle exists from the start, so its first Show is quick.
        var pill = _container.Resolve<PillWindow>();
        new WindowInteropHelper(pill).EnsureHandle();

        // 8. Dictation. The controller is created here, on the UI thread, because it captures this thread's
        // SynchronizationContext; hook signals reach it through the dispatcher.
        var controller = _container.Resolve<DictationController>();
        StartInputHooks(_container, controller);

        // 9. Tray
        _tray = _container.Resolve<TrayIconHost>();
        _tray.OpenRequested += ShowMainWindow;
        _tray.QuitRequested += Quit;
        _tray.Show();

        // 10. Speech engine: downloads the model if needed, loads it and warms it up in the background. Its status
        // changes reach the main window as a notification; subscribed first, so even the Downloading and Loading
        // statuses are reported.
        var engine = _container.Resolve<ISpeechEngine>();
        var mediator = _container.Resolve<IMediator>();
        engine.StatusChanged += status => mediator.Publish(new EngineStatusChangedNotification(status)).FireAndForget();
        engine.InitializeAsync(_appCts.Token).FireAndForget();

        // 11. Start with Windows
        ApplyStartupRegistration(_container);

        // 12. Second launches
        _guard.ListenForShowRequests(ShowMainWindow);

        // 13. Started at sign-in, Murmur stays in the tray.
        if (!args.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase))
            ShowMainWindow();
    }

    /// <summary>Leaves nothing behind (no global hooks, no tray icon, no claim on the single instance), says why, and exits.</summary>
    private void FailStartup(Exception ex)
    {
        Log.Fatal(ex, "Murmur could not start");
        StopInputAndTray();
        _guard?.Dispose();

        MessageBox.Show(
            $"Murmur couldn't start: {ex.GetBaseException().Message}{Environment.NewLine}{Environment.NewLine}" +
            $"Details are in the log in {MurmurPaths.Default.LogsDirectory}.",
            "Murmur",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Sign-out or shutdown: WPF shuts the app down next, so the main window must really close.
        IsQuitting = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopInputAndTray();

        // Disposes the controller (releasing the microphone) and the rest; the speech engine is not container-owned.
        _container?.Dispose();
        _guard?.Dispose();

        Log.Information("Murmur stopped");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureLogging()
    {
        var logPath = Path.Combine(MurmurPaths.Default.LogsDirectory, "Murmur-.log");

        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .Enrich.FromLogContext()
            .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                fileSizeLimitBytes: 50_000_000,
                rollOnFileSizeLimit: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private void StartInputHooks(IContainer container, DictationController controller)
    {
        var dispatcher = Dispatcher;
        _inputHooks = new InputHookHost(
            container.Resolve<HotkeyMatcher>(),
            container.Resolve<HotkeyCaptureCoordinator>(),
            container.Resolve<IKeyboardSender>(),
            container.Resolve<TimeProvider>(),
            (signal, at) => dispatcher.BeginInvoke(() => controller.HandleSignal(signal, at)));

        try
        {
            _inputHooks.Start();
        }
        catch (Exception ex)
        {
            // Murmur still runs (history, settings), but the hotkey cannot work.
            Log.Error(ex, "Could not install the keyboard hook; dictation is unavailable");
        }
    }

    private static void ApplyStartupRegistration(IContainer container)
    {
        try
        {
            var settings = container.Resolve<ISettingsService>().Load();
            container.Resolve<IStartupRegistration>().Apply(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not update the start-with-Windows entry");
        }
    }

    private void ShowMainWindow()
    {
        if (IsQuitting || _mainWindow is null)
            return;

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void Quit()
    {
        if (IsQuitting)
            return;

        Log.Information("Quit requested");
        StopInputAndTray();

        // The speech engine is deliberately not disposed: freeing the native model while a transcription may still be
        // running can crash, and process exit frees the GPU memory.
        Shutdown();
    }

    /// <summary>Stops taking hotkeys, removes the tray icon and cancels background work (the model download).</summary>
    private void StopInputAndTray()
    {
        IsQuitting = true;
        _inputHooks?.Dispose();
        _tray?.Dispose();
        _appCts.Cancel();
    }

    private static bool IsWebView2RuntimeInstalled()
    {
        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrEmpty(version);
        }
        catch
        {
            return false;
        }
    }
}
