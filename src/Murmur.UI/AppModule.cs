using System.Net.Http;
using Autofac;
using Murmur.Core.Audio;
using Murmur.Core.Dictation;
using Murmur.Core.Input;
using Murmur.Core.Insertion;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.Core.Text;
using Murmur.Interop;
using Murmur.UI.Pill;
using Murmur.UI.Tray;
using Murmur.WebView;

namespace Murmur.UI;

public class AppModule : Module
{
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

        builder.RegisterType<ModelManager>().SingleInstance();
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

#if DEBUG
        builder.RegisterType<NoopStartupRegistration>().As<IStartupRegistration>().SingleInstance();
#else
        builder.Register(_ => new StartupRegistration(Environment.ProcessPath!)).As<IStartupRegistration>().SingleInstance();
#endif
    }

    private static void RegisterDictation(ContainerBuilder builder)
    {
        builder.Register(c => new HotkeyMatcher(c.Resolve<ISettingsService>().Load().Hotkey)).SingleInstance();
        builder.RegisterType<HotkeyCaptureCoordinator>().SingleInstance();
        builder.Register(_ => new DictationStateMachine(DictationTimings.Default)).SingleInstance();
        builder.RegisterType<SoundCues>().As<ISoundCues>().SingleInstance();

        // Resolve on the UI thread: it captures that thread's SynchronizationContext.
        builder.RegisterType<DictationController>().SingleInstance();
    }
}
