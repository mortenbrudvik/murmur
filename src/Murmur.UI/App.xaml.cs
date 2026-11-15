using System.IO;
using System.Windows;
using System.Windows.Interop;
using Autofac;
using MediatR;
using Murmur.Common;
using Murmur.Core.Audio;
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
using Murmur.UI.Splash;
using Murmur.UI.Themes;
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

    /// <summary>The splash while it is up; null on a sign-in start and once it has handed over to the main window.</summary>
    private SplashWindow? _splash;
    private SplashViewModel? _splashModel;
    private SplashSession? _splashSession;

    // Knows whether Murmur is really exiting; until then closing the main window only hides it. Null before the
    // container exists, when there is no window to close yet.
    private AppLifecycle? _lifecycle;

    private bool IsQuitting => _lifecycle?.IsQuitting == true;

    // A property, not a field: App exists before ConfigureLogging runs, and a context logger taken then would wrap the
    // silent default logger for good.
    private static ILogger Logger => Log.ForContext<App>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RunStartup(() => Start(e.Args));
    }

    /// <summary>
    /// Runs a part of start-up and ends Murmur with a message if it throws. Start-up runs as dispatcher operations, so
    /// an exception escaping one would be swallowed by the DispatcherUnhandledException handler and leave a process
    /// with no window, perhaps no tray icon, and the single-instance mutex held, so that every later launch would only
    /// signal it and exit.
    /// </summary>
    private void RunStartup(Action part)
    {
        try
        {
            part();
        }
        catch (Exception ex)
        {
            FailStartup(ex);
        }
    }

    private void Start(string[] args)
    {
        // 1. Logging
        ConfigureLogging();
        Logger.Information("Murmur starting");
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
            Logger.Error(args.Exception, "UI thread exception");
            args.Handled = args.Exception is not (OutOfMemoryException or StackOverflowException);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Logger.Fatal(ex, "Unhandled exception");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        // 3. Single instance: a second launch shows the running instance's window and exits. A restarted Murmur is
        // started by the instance it replaces, which is still shutting down, so it waits for that one to let go.
        _guard = new SingleInstanceGuard();
        if (!_guard.TryAcquire(launch.Restarted ? RestartWait : TimeSpan.Zero))
        {
            if (launch.Restarted)
                Logger.Warning("The Murmur that started this one did not exit within {Wait}", RestartWait);

            _guard.SignalExisting();
            Shutdown();
            return;
        }

        // 4. Murmur lives in the tray: windows opening and closing never end it, only Quit does.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // The theme is fixed to dark and applied before any window is created to avoid a flicker.
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        // The splash goes up now, before the container, the main window and WebView2 exist: that wait is what it is
        // for. Not on a sign-in start, which stays silent in the tray. The package probe is the one the container
        // registers later; it is cheap and has no state.
        if (ShouldShowSplash(launch, new PackageIdentity()))
        {
            ShowSplash();

            // The rest of start-up takes the UI thread for a good part of a second (measured: about 0.4 s on the
            // owner's PC), and a window is only drawn when the dispatcher is free. So it waits one turn: the splash is
            // drawn first and start-up goes on behind it. Its animation and its clock begin when start-up is done and
            // frames can be drawn again; otherwise the animation's opening would never be seen and the splash would
            // close before its minimum time on screen.
            StartupTurn.AfterRender(Dispatcher, () => RunStartup(() =>
            {
                FinishStart(launch);
                BeginSplash();
            }));
            return;
        }

        FinishStart(launch);
    }

    /// <summary>Everything from the container on: steps 5 to 13. Runs behind the splash when there is one.</summary>
    private void FinishStart(LaunchArguments launch)
    {
        // 5. Container and API
        _container = AutofacBootstrapper.Bootstrap();
        _lifecycle = _container.Resolve<AppLifecycle>();

        // Set once, here, before the main window exists and so before any IPC can read or take it.
        _container.Resolve<LaunchOptions>().OpenPage = launch.Page;

        // Decides how Murmur starts with Windows (see AppModule.RegisterStartup) and how it tells a sign-in start.
        var identity = _container.Resolve<IPackageIdentity>();
        Logger.Information("Running {Channel}", identity.IsPackaged ? "from its MSIX package" : "unpackaged");

        var registry = _container.Resolve<ApiRegistry>();
        registry.Register<GetHistoryRequest>("history", "get");
        registry.Register<DeleteHistoryEntryRequest>("history", "delete");
        registry.Register<ClearHistoryRequest>("history", "clear");
        registry.Register<CopyHistoryEntryRequest>("history", "copy");
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
        registry.Register<QuitAppRequest>("app", "quit");
        registry.Register<GetInputStatusRequest>("app", "getInputStatus");
        registry.Register<GetAboutRequest>("app", "getAbout");
        registry.Register<UiReadyRequest>("app", "ready");

        // The history file is read on first use, and the first dictation of a sign-in start would otherwise wait for
        // that read; touching the store here does it off the UI thread while the rest of start-up goes on.
        PreloadHistoryAsync(_container.Resolve<IHistoryStore>()).FireAndForget();

        // 6. The splash closes once the web UI has drawn its first page (and its minimum time has passed).
        _container.Resolve<UiReadiness>().BecameReady += () => _splashSession?.MarkUiReady();

        // Created before the pill and set explicitly: WPF makes the first window it sees the MainWindow, and that
        // must never be the pill or the splash. Its WebView only starts when the window is first shown.
        _mainWindow = _container.Resolve<MainWindow>();
        MainWindow = _mainWindow;

        // The preloaded window is shown but not drawn (see MainWindow.Preload). Its taskbar button is there, and a
        // click on it activates a window nobody can see: take that as "show me the window now".
        _mainWindow.Activated += (_, _) => SkipSplash();

        // 7. The pill's window handle exists from the start, so its first Show is quick. Its colours follow a Windows
        // contrast theme.
        PillContrast.Follow(Resources);
        var pill = _container.Resolve<PillWindow>();
        new WindowInteropHelper(pill).EnsureHandle();

        // 8. Tray. Before the hotkey hooks, so that a hook failure always has an icon to report through.
        _tray = _container.Resolve<TrayIconHost>();
        _tray.OpenRequested += ShowMainWindow;

        // Resolved here, not when first asked: it must be listening before the web UI can report that it has drawn.
        var about = _container.Resolve<AboutLauncher>();
        _tray.AboutRequested += () =>
        {
            ShowMainWindow();
            about.Show();
        };
        _tray.QuitRequested += Quit;
        _tray.Show();

        // 9. Dictation. The controller is created here, on the UI thread, because it captures this thread's
        // SynchronizationContext; hook signals reach it through the dispatcher. The hook starts in the background
        // from here on, because a refused hook is tried once more after a short wait.
        var controller = _container.Resolve<DictationController>();
        StartInputHooksAsync(_container, controller).FireAndForget();

        // With "keep the microphone ready" on, the microphone opens now, in the background, so that the first
        // dictation is as quick as the ones after it.
        KeepMicrophoneReady(_container.Resolve<MicrophoneReadiness>(), _container.Resolve<ISettingsService>());

        // "Restart Murmur" and "Quit Murmur" are asked for by IPC handlers, on a pool thread. A restart has already
        // started the new Murmur, which waits for this one to let go of the single-instance mutex (RestartWait).
        _lifecycle.RestartRequested += () => Dispatcher.BeginInvoke(Quit);
        _lifecycle.QuitRequested += () => Dispatcher.BeginInvoke(Quit);

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

        // The splash's status line follows the engine too, for as long as the splash is up.
        _splashModel?.Apply(engine.Status);
        engine.StatusChanged += status => Dispatcher.BeginInvoke(() => _splashModel?.Apply(status));

        StartSpeechAsync(engine, _container.Resolve<IGpuInfo>(), _container.Resolve<SetupCoordinator>()).FireAndForget();

        // 11. Start with Windows
        ApplyStartupRegistration(_container);

        // 12. Second launches. The guard was acquired in step 3, before this part of start-up.
        _guard!.ListenForShowRequests(ShowMainWindow);

        // 13. Started at sign-in, Murmur stays in the tray. Otherwise the window opens: behind the splash while that
        // is up, so its page loads unseen and the splash hands over to a window that is already drawn.
        if (!IsSignInStart(launch, identity))
        {
            if (_splashSession is { IsOver: false })
                _mainWindow.Preload();
            else
                ShowMainWindow();
        }
    }

    /// <summary>
    /// Puts the splash on screen, at rest: its animation and its clock start in <see cref="BeginSplash"/>. When it
    /// closes is the gate's decision: the UI has drawn and the minimum time has passed, the user skipped it, or the
    /// timeout ran out.
    /// </summary>
    private void ShowSplash()
    {
        _splashModel = new SplashViewModel(new AppInfo(new PackageIdentity(), MurmurPaths.Default).Version);
        var splash = _splash = new SplashWindow(_splashModel);

        // WPF made the splash the MainWindow, being the first window created. It is not: see step 6.
        MainWindow = null;

        var gate = new SplashGate(TimeProvider.System, SplashGate.DefaultMinimum, SplashGate.DefaultTimeout);
        var session = _splashSession = new SplashSession(
            gate,
            mainWindowIsShown: () => !IsQuitting && _mainWindow is { IsVisible: true },
            revealMainWindow: activate => _mainWindow?.Reveal(activate),
            closeSplash: () => CloseSplashWindow(splash),
            foregroundProcessId: WindowNative.ForegroundProcessId,
            ownProcessId: (uint)Environment.ProcessId);

        splash.SkipRequested += session.Skip;

        // Raised on whichever thread closed the gate: a timer's, the IPC call's that reported the UI ready, or this one.
        gate.Closed += reason => Dispatcher.BeginInvoke(() =>
        {
            Logger.Information("The splash closes: {Reason}", reason);
            session.HandOver();
        });
        splash.Show();
    }

    /// <summary>
    /// Start-up is done and the UI thread is free: the splash's animation starts, and with it the clock that decides
    /// when it closes. Nothing to do if it was closed in the meantime.
    /// </summary>
    private void BeginSplash()
    {
        if (_splashSession is not { IsOver: false } session)
            return;

        _splash?.Play();
        session.Begin();
    }

    /// <summary>Closes the splash now, if it is up: the still unseen main window was activated from the taskbar.</summary>
    private void SkipSplash() => _splashSession?.Skip();

    /// <summary>
    /// The splash is over: it fades out after a hand-over, and goes at once when Murmur is quitting. Either way the
    /// status line has nothing left to follow.
    /// </summary>
    private void CloseSplashWindow(SplashWindow splash)
    {
        _splash = null;
        _splashModel = null;
        if (IsQuitting)
            splash.Close();
        else
            splash.FadeOutAndCloseAsync().FireAndForget();
    }

    /// <summary>
    /// Reads the history file into memory. A failure is only logged: the store retries the read on its next use, and
    /// reports the failure to whoever asks for it then.
    /// </summary>
    private static async Task PreloadHistoryAsync(IHistoryStore history)
    {
        try
        {
            await Task.Run(() => history.Query(null, 1, null));
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not pre-load the history; it will be read when first needed");
        }
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
            Logger.Error(ex, "The first-run decision failed; starting the speech engine anyway");
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
    /// The splash is for a start that goes on to show the window: started by hand, or restarted from Settings. Murmur
    /// starting with Windows stays silent in the tray.
    /// </summary>
    internal static bool ShouldShowSplash(LaunchArguments launch, IPackageIdentity identity) =>
        !IsSignInStart(launch, identity);

    /// <summary>Shows what <see cref="TrayNotifier"/> finds worth a notification. Runs on the UI thread.</summary>
    private void NotifyTray(TrayNotifier notifier, ISettingsService settings, InputStatus input, EngineStatus status)
    {
        if (IsQuitting)
            return;

        // Only a ready notice names the hotkey, so the settings are not read for every download progress update.
        var defaultName = HotkeyDefinition.Default.DisplayName;
        var hotkeyName = status.State == EngineState.Ready ? settings.ReadHotkeyName(fallback: defaultName) : defaultName;
        var notice = notifier.OnStatus(status, _mainWindow?.IsActive == true, hotkeyName, input.HookInstalled);
        if (notice is not null)
            _tray?.ShowNotification(notice.Title, notice.Message);
    }

    /// <summary>
    /// Applies the saved "keep the microphone ready" setting. Settings that cannot be read leave it off, which is its
    /// default; that must not keep Murmur from starting.
    /// </summary>
    private static void KeepMicrophoneReady(MicrophoneReadiness microphone, ISettingsService settings)
    {
        try
        {
            microphone.Apply(settings.Load());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning(ex, "Could not read the settings to keep the microphone ready");
        }
    }

    /// <summary>Leaves nothing behind (no global hooks, no tray icon, no claim on the single instance), says why, and exits.</summary>
    private void FailStartup(Exception ex)
    {
        Logger.Fatal(ex, "Murmur could not start");
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
        _lifecycle?.MarkQuitting();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopInputAndTray();

        // Disposes the controller (releasing the microphone) and the rest; the speech engine is not container-owned.
        _container?.Dispose();
        _guard?.Dispose();

        Logger.Information("Murmur stopped");
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
    /// Starts the keyboard hook, which <see cref="InputHookStarter"/> tries twice without holding up the UI thread. What
    /// the hook needs is resolved here, before the first attempt, so a service that cannot be built still fails the
    /// start-up instead of passing for a refused hook.
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
        // Persistent: the hotkey stays off for the whole run, so no later notice may push this one out.
        _tray?.ShowNotification(TrayNotifier.HotkeyUnavailable.Title, TrayNotifier.HotkeyUnavailable.Message, persistent: true);
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
            Logger.Warning(ex, "Could not update the start-with-Windows entry");
        }
    }

    private void ShowMainWindow()
    {
        if (IsQuitting || _mainWindow is null)
            return;

        // While the splash is up the window is on its way: asking for it (the tray, a second launch) cuts the splash
        // short, and the hand-over brings the window to the front.
        if (_splashSession is { IsOver: false } session)
        {
            session.RequestWindow();
            return;
        }

        _mainWindow.Reveal(activate: true);
    }

    private void Quit()
    {
        if (IsQuitting)
            return;

        Logger.Information("Quit requested");
        StopInputAndTray();

        // The speech engine is deliberately not disposed: freeing the native model while a transcription may still be
        // running can crash, and process exit frees the GPU memory.
        Shutdown();
    }

    /// <summary>Stops taking hotkeys, removes the tray icon and cancels background work (the model download).</summary>
    private void StopInputAndTray()
    {
        _lifecycle?.MarkQuitting();

        // A splash still up goes at once, without its hand-over.
        _splashSession?.Abandon();


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
        catch (Exception ex)
        {
            // Usually WebView2RuntimeNotFoundException, but any failure is reported to the user as a missing runtime,
            // so the log must say which it was.
            Logger.Warning(ex, "Could not find the WebView2 runtime");
            return false;
        }
    }
}
