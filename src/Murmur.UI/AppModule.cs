using System.Net.Http;
using Autofac;
using MediatR;
using Murmur.Core.Audio;
using Murmur.Core.Dictation;
using Murmur.Core.Hardware;
using Murmur.Core.Input;
using Murmur.Core.Insertion;
using Murmur.Core.Services;
using Murmur.Core.Setup;
using Murmur.Core.Speech;
using Murmur.Core.Text;
using Murmur.Interop;
using Murmur.UI.Pill;
using Murmur.UI.Tray;
using Murmur.WebView;

namespace Murmur.UI;

public class AppModule : Module
{
#if DEBUG
    private const bool IsDebugBuild = true;
#else
    private const bool IsDebugBuild = false;
#endif

    // Probed here rather than resolved: this module is configuring the container, so it cannot ask it a question.
    private readonly IPackageIdentity _identity = new PackageIdentity();

    protected override void Load(ContainerBuilder builder)
    {
        // WebView
        builder.RegisterType<WebViewControl>()
            .AsSelf()
            .As<IWebViewInterop>()
            .SingleInstance();

        // Interop Bridge
        builder.RegisterType<ApiBridge>().SingleInstance();
        builder.RegisterType<ApiEventAggregator>().SingleInstance();
        builder.RegisterType<ApiRegistry>().SingleInstance();

        RegisterCore(builder);
        RegisterStartup(builder, _identity, IsDebugBuild);
        RegisterDictation(builder);

        // ViewModels
        builder.RegisterType<MainViewModel>().SingleInstance();
        builder.RegisterType<PillViewModel>().SingleInstance();

        // Views
        builder.RegisterType<MainWindow>().SingleInstance();
        builder.RegisterType<PillWindow>().SingleInstance();
        builder.RegisterType<PillPresenter>().As<IPillPresenter>().SingleInstance();
        builder.RegisterType<TrayIconHost>().SingleInstance();
    }

    private static void RegisterCore(ContainerBuilder builder)
    {
        builder.RegisterInstance(MurmurPaths.Default);
        builder.RegisterInstance(TimeProvider.System);
        builder.Register(_ => new HttpClient()).SingleInstance();

        builder.RegisterType<SettingsService>().As<ISettingsService>().SingleInstance();
        builder.RegisterType<VocabularyService>().As<IVocabularyService>().SingleInstance();
        builder.RegisterType<HistoryStore>().As<IHistoryStore>().SingleInstance();
        builder.RegisterType<AudioArchive>().As<IAudioArchive>().SingleInstance();
        builder.RegisterType<AudioRecorder>().As<IAudioRecorder>().SingleInstance();
        // The setup page's microphone test has a recorder of its own, so it never collides with a dictation. Owned by
        // the container, which disposes it on exit and so stops a test that is still running.
        builder.Register(c => new MicrophoneLevelMonitor(() => new AudioRecorder(), c.Resolve<IMediator>(), c.Resolve<TimeProvider>()))
            .SingleInstance();

        builder.RegisterType<DiskSpace>().As<IDiskSpace>().SingleInstance();
        builder.RegisterType<ModelManager>().As<IModelManager>().SingleInstance();
        builder.RegisterType<GpuInfo>().As<IGpuInfo>().SingleInstance();
        builder.RegisterType<CudaRuntimeLocator>().As<ICudaRuntimeLocator>().SingleInstance();
        builder.RegisterType<ModelSelection>().SingleInstance();
        builder.RegisterType<LaunchOptions>().SingleInstance();
        builder.RegisterType<SetupCoordinator>().SingleInstance();
        builder.RegisterType<ShellLinkOpener>().As<ILinkOpener>().SingleInstance();
        // Also as itself, so that App can subscribe to RestartRequested, which the interface does not carry.
        builder.RegisterType<AppLifecycle>().AsSelf().As<IAppLifecycle>().SingleInstance();
        builder.RegisterType<WhisperModelLoader>().As<IWhisperModelLoader>().SingleInstance();
        // Never disposed by the container: disposing the native Whisper factory while a transcription may still be
        // running can crash the process, and process exit frees the GPU memory anyway.
        builder.RegisterType<WhisperEngine>().As<ISpeechEngine>().SingleInstance().ExternallyOwned();

        builder.RegisterType<TranscriptCleaner>().As<ITranscriptPostProcessor>().SingleInstance();
        builder.RegisterType<VocabularyReplacer>().As<ITranscriptPostProcessor>().SingleInstance();

        builder.RegisterType<Win32Clipboard>().As<IClipboard>().SingleInstance();
        builder.RegisterType<Win32ForegroundInfo>().As<IForegroundInfo>().SingleInstance();
        builder.RegisterType<Win32KeyboardSender>().As<IKeyboardSender>().SingleInstance();
        builder.RegisterType<Win32KeyStateProbe>().As<IKeyStateProbe>().SingleInstance();
        builder.RegisterType<TextInserter>().As<ITextInserter>().SingleInstance();
    }

    /// <summary>
    /// Where package identity decides how Murmur starts with Windows. Inside a package a Run key write lands in a
    /// private hive that Explorer never reads and still reports success, so the wrong choice here is invisible at run
    /// time. Debug builds never register, so running from a development folder never adds that folder's executable to
    /// the owner's sign-in programs. The probe is registered as well, for App to ask how it was started.
    /// </summary>
    internal static void RegisterStartup(ContainerBuilder builder, IPackageIdentity identity, bool debugBuild)
    {
        builder.RegisterInstance(identity).As<IPackageIdentity>();

        if (debugBuild)
            builder.RegisterType<NoopStartupRegistration>().As<IStartupRegistration>().SingleInstance();
        else if (identity.IsPackaged)
            builder.RegisterType<PackagedStartupRegistration>().As<IStartupRegistration>().SingleInstance();
        else
            builder.Register(_ => new StartupRegistration(Environment.ProcessPath!)).As<IStartupRegistration>().SingleInstance();
    }

    private static void RegisterDictation(ContainerBuilder builder)
    {
        builder.Register(c => new HotkeyMatcher(c.Resolve<ISettingsService>().Load().Hotkey)).SingleInstance();
        builder.RegisterType<HotkeyCaptureCoordinator>().SingleInstance();
        // Written by App once the keyboard hook has started (or failed to); read by the tray and the web app.
        builder.RegisterType<InputStatus>().SingleInstance();
        builder.Register(_ => new DictationStateMachine(DictationTimings.Default)).SingleInstance();
        builder.RegisterType<SoundCues>().As<ISoundCues>().SingleInstance();

        // Resolve on the UI thread: it captures that thread's SynchronizationContext.
        builder.RegisterType<DictationController>().SingleInstance();
    }
}
