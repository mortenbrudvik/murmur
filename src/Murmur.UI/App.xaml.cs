using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using Autofac;
using MediatR;
using Murmur.Common;
using Murmur.Core.Dictation;
using Murmur.Core.Features.App;
using Murmur.Core.Features.Engine;
using Murmur.Core.Features.History;
using Murmur.Core.Features.Models;
using Murmur.Core.Features.Settings;
using Murmur.Core.Features.Setup;
using Murmur.Core.Features.Vocabulary;
using Murmur.Core.Hardware;
using Murmur.Core.Input;
using Murmur.Core.Insertion;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Setup;
using Murmur.Core.Speech;
using Murmur.Interop;
using Murmur.UI.Pill;
using Murmur.UI.Tray;
using Serilog;
using Wpf.Ui.Appearance;

namespace Murmur.UI;

public partial class App : Application
{
    /// <summary>
    /// How long a restarted Murmur waits for the instance that started it to let go of the single-instance mutex. That
    /// one is only closing, but disposing the container can take a moment.
    /// </summary>
    private static readonly TimeSpan RestartWait = TimeSpan.FromSeconds(10);

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
        var launch = LaunchArguments.Parse(args);

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

        // 3. Single instance: a second launch shows the running instance's window and exits. A restarted Murmur is
        // started by the instance it replaces, which is still shutting down, so it waits for that one to let go.
        _guard = new SingleInstanceGuard();
        if (!_guard.TryAcquire(launch.Restarted ? RestartWait : TimeSpan.Zero))
        {
            if (launch.Restarted)
                Log.Warning("The Murmur that started this one did not exit within {Wait}", RestartWait);

            _guard.SignalExisting();
            Shutdown();
            return;
        }

        // 4. Murmur lives in the tray: windows opening and closing never end it, only Quit does.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 5. Container and API
        _container = AutofacBootstrapper.Bootstrap();

        // Set once, here, before any window exists and so before any IPC can read or take it.
        _container.Resolve<LaunchOptions>().OpenPage = launch.Page;

        // Decides how Murmur starts with Windows (see AppModule.RegisterStartup) and how it tells a sign-in start.
        var identity = _container.Resolve<IPackageIdentity>();
        Log.Information("Running {Channel}", identity.IsPackaged ? "from its MSIX package" : "unpackaged");

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
        registry.Register<GetSetupStateRequest>("setup", "getState");
        registry.Register<UpdateSetupStateRequest>("setup", "update");
        registry.Register<StartMicTestRequest>("setup", "startMicTest");
        registry.Register<StopMicTestRequest>("setup", "stopMicTest");
        registry.Register<GetModelChoiceRequest>("models", "getChoice");
        registry.Register<SetModelChoiceRequest>("models", "setChoice");
        registry.Register<OpenLinkRequest>("app", "openLink");
        registry.Register<RestartAppRequest>("app", "restart");
        registry.Register<GetInputStatusRequest>("app", "getInputStatus");

        // 6. The theme is fixed to dark and applied before any window is created to avoid a flicker.
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        // Created before the pill and set explicitly: WPF makes the first window it sees the MainWindow, and that
        // must never be the pill. Its WebView only starts when the window is first shown.
        _mainWindow = _container.Resolve<MainWindow>();
        MainWindow = _mainWindow;

        // 7. The pill's window handle exists from the start, so its first Show is quick.
        var pill = _container.Resolve<PillWindow>();
        new WindowInteropHelper(pill).EnsureHandle();

        // 8. Tray. Before the hotkey hooks, so that a hook failure always has an icon to report through.
        _tray = _container.Resolve<TrayIconHost>();
        _tray.OpenRequested += ShowMainWindow;
        _tray.QuitRequested += Quit;
        _tray.Show();

        // 9. Dictation. The controller is created here, on the UI thread, because it captures this thread's
        // SynchronizationContext; hook signals reach it through the dispatcher. The hook starts in the background
        // from here on, because a refused hook is tried once more after a short wait.
        var controller = _container.Resolve<DictationController>();
        StartInputHooksAsync(_container, controller).FireAndForget();

        // "Restart Murmur" is asked for by an IPC handler, on a pool thread.
        _container.Resolve<AppLifecycle>().RestartRequested += () => Dispatcher.BeginInvoke(Restart);

        // 10. Speech engine: after the first-run decision, downloads the model if needed, loads it and warms it up in
        // the background. Its status changes reach the main window as a notification; subscribed first, so even the
        // Downloading and Loading statuses are reported.
        var engine = _container.Resolve<ISpeechEngine>();
        var mediator = _container.Resolve<IMediator>();
        engine.StatusChanged += status => mediator.Publish(new EngineStatusChangedNotification(status)).FireAndForget();

        // Windows notifications through the tray icon, for what happens while the window is not in front: the model is
        // ready after a download, the engine failed, the download has no internet. The notifier's state lives on the
        // UI thread, where the icon is.
        var notifier = new TrayNotifier();
        var settings = _container.Resolve<ISettingsService>();
        var input = _container.Resolve<InputStatus>();
        engine.StatusChanged += status => Dispatcher.BeginInvoke(() => NotifyTray(notifier, settings, input, status));

        StartSpeechAsync(engine, _container.Resolve<IGpuInfo>(), _container.Resolve<SetupCoordinator>()).FireAndForget();

        // 11. Start with Windows
        ApplyStartupRegistration(_container);

        // 12. Second launches
        _guard.ListenForShowRequests(ShowMainWindow);

        // 13. Started at sign-in, Murmur stays in the tray.
        if (!IsSignInStart(launch, identity))
            ShowMainWindow();
    }

    /// <summary>
    /// Decides first-run setup, then starts the speech engine, which would otherwise pick the model before setup has
    /// looked at what is on disk. Both the GPU detection (its first read starts the CUDA driver) and the decision read
    /// files and drivers, so they run off the UI thread, and the detection first, so that a later model-choice request
    /// finds it done. A failed decision is logged and never keeps the engine from starting.
    /// </summary>
    private async Task StartSpeechAsync(ISpeechEngine engine, IGpuInfo gpu, SetupCoordinator setup)
    {
        try
        {
            await Task.Run(() =>
            {
                _ = gpu.Report;
                setup.Initialize();
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The first-run decision failed; starting the speech engine anyway");
        }

        await engine.InitializeAsync(_appCts.Token);
    }

    /// <summary>
    /// The Run key starts Murmur with <c>--minimized</c>. The Store package's startup task cannot pass arguments, so a
    /// packaged start asks Windows how it was activated instead. A restart is never a sign-in start: the user just asked
    /// for it and wants to see the window.
    /// </summary>
    internal static bool IsSignInStart(LaunchArguments launch, IPackageIdentity identity) =>
        !launch.Restarted
        && (launch.Minimized || (identity.IsPackaged && PackagedStartupRegistration.IsStartupActivation()));

    /// <summary>
    /// Starts a new Murmur on Settings and quits this one. The new instance waits for this one to let go of the
    /// single-instance mutex (<see cref="RestartWait"/>). If the new one cannot be started, this one keeps running.
    /// </summary>
    private void Restart()
    {
        if (IsQuitting)
            return;

        Log.Information("Restarting Murmur");
        try
        {
            // Whether a child started this way keeps the MSIX package identity is checked in Task 11 of the first-run
            // setup plan (packaged install); until then, no fallback.
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, LaunchArguments.RestartArguments) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not start a new Murmur; staying open");
            return;
        }

        Quit();
    }

    /// <summary>Shows what <see cref="TrayNotifier"/> finds worth a notification. Runs on the UI thread.</summary>
    private void NotifyTray(TrayNotifier notifier, ISettingsService settings, InputStatus input, EngineStatus status)
    {
        if (IsQuitting)
            return;

        // Only a ready notice names the hotkey, so the file is not read for every download progress update.
        var hotkeyName = status.State == EngineState.Ready ? ReadHotkeyName(settings) : HotkeyDefinition.Default.DisplayName;
        var notice = notifier.OnStatus(status, _mainWindow?.IsActive == true, hotkeyName, input.HookInstalled);
        if (notice is not null)
            _tray?.ShowNotification(notice.Title, notice.Message);
    }

    private static string ReadHotkeyName(ISettingsService settings)
    {
        try
        {
            return settings.Load().Hotkey.DisplayName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not read the hotkey for the tray notification");
            return HotkeyDefinition.Default.DisplayName;
        }
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

    /// <summary>
    /// Starts the keyboard hook, which <see cref="InputHookStarter"/> tries twice: the first attempt runs here, the wait
    /// and the second attempt do not hold up the UI thread. What the hook needs is resolved before the first attempt,
    /// so a service that cannot be built still fails the start-up instead of passing for a refused hook.
    /// </summary>
    private Task StartInputHooksAsync(IContainer container, DictationController controller)
    {
        var dispatcher = Dispatcher;
        var matcher = container.Resolve<HotkeyMatcher>();
        var coordinator = container.Resolve<HotkeyCaptureCoordinator>();
        var keyboard = container.Resolve<IKeyboardSender>();
        var time = container.Resolve<TimeProvider>();
        var starter = new InputHookStarter(
            () => new InputHookHost(
                matcher, coordinator, keyboard, time,
                (signal, at) => dispatcher.BeginInvoke(() => controller.HandleSignal(signal, at))),
            time);

        return KeepInputHooksAsync(starter, container.Resolve<InputStatus>(), container.Resolve<IMediator>());
    }

    /// <summary>
    /// Keeps the host that <paramref name="starter"/> started for as long as Murmur runs. If the hook cannot be installed,
    /// Murmur keeps running (history, settings) but the hotkey cannot work, so it says so: the tray tooltip and a
    /// notification, and the main window's alert (through <see cref="InputStatus"/>, and a push for a window that
    /// asked before the second attempt ended).
    /// </summary>
    private async Task KeepInputHooksAsync(InputHookStarter starter, InputStatus input, IMediator mediator)
    {
        // Resumes on the UI thread, where Quit also runs.
        var hooks = await starter.StartAsync();
        if (IsQuitting)
        {
            // Murmur began to quit during the wait, after StopInputAndTray found no hooks to remove.
            hooks?.Dispose();
            return;
        }

        if (hooks is not null)
        {
            _inputHooks = hooks;
            return;
        }

        input.Error = starter.Failure?.Message;
        input.HookInstalled = false;

        _tray?.Refresh();
        _tray?.ShowNotification(TrayNotifier.HotkeyUnavailable.Title, TrayNotifier.HotkeyUnavailable.Message);
        mediator.Publish(new InputStatusChangedNotification(input)).FireAndForget();
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
