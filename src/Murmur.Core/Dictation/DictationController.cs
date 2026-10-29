using MediatR;
using Murmur.Core.Audio;
using Murmur.Core.Features.History;
using Murmur.Core.Input;
using Murmur.Core.Insertion;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.Core.Text;
using Serilog;

namespace Murmur.Core.Dictation;

/// <summary>
/// Runs dictation end to end. Hotkey signals drive the <see cref="DictationStateMachine"/>; its actions start and
/// stop the microphone, transcribe, post-process, paste and record history, with the pill and sound cues as
/// feedback.
/// <para>
/// Single-threaded by design: everything runs on the thread that created the controller (the UI thread). Recorder
/// events and timer ticks are posted to that thread's <see cref="SynchronizationContext"/>, or run inline when it
/// has none (tests). Only the paste itself runs on the thread pool.
/// </para>
/// </summary>
public sealed class DictationController : IDisposable
{
    private const string LoadingModelNotice = "Loading model…";
    private const string CopiedNotice = "Copied — press Ctrl+V";
    private const string SavedNotice = "Saved to history";
    private const string MicrophoneUnavailableNotice = "Microphone unavailable";
    private const string TranscriptionFailedNotice = "Transcription failed";

    /// <summary>
    /// Whisper's own silence verdict. The speech detector is the real gate (Whisper.net reports 0 here); this is a
    /// harmless second guard.
    /// </summary>
    private const float NoSpeechThreshold = 0.6f;

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    private readonly ILogger _logger = Log.ForContext<DictationController>();
    private readonly DictationStateMachine _machine;
    private readonly HotkeyMatcher _matcher;
    private readonly IAudioRecorder _recorder;
    private readonly ISpeechEngine _engine;
    private readonly ITranscriptPostProcessor[] _postProcessors;
    private readonly ITextInserter _inserter;
    private readonly IHistoryStore _history;
    private readonly IAudioArchive _audioArchive;
    private readonly IPillPresenter _pill;
    private readonly ISoundCues _cues;
    private readonly ISettingsService _settingsService;
    private readonly IVocabularyService _vocabulary;
    private readonly IKeyStateProbe _keys;
    private readonly IMediator _mediator;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _context;
    private readonly SpeechDetector _detector = new();

    // Identifies the current dictation. Bumped when one starts and when one is abandoned (Esc, microphone failure),
    // so recorder events, ticks and completions that belong to an older one recognise themselves as stale.
    // Written on the UI thread, read on the capture thread.
    private int _generation;

    private MurmurSettings _settings = MurmurSettings.Default; // snapshot taken when a recording starts
    private bool _recording;
    private bool _micLive;
    private ITimer? _timer;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public DictationController(
        DictationStateMachine machine, HotkeyMatcher matcher, IAudioRecorder recorder, ISpeechEngine engine,
        IEnumerable<ITranscriptPostProcessor> postProcessors, ITextInserter inserter, IHistoryStore history,
        IAudioArchive audioArchive, IPillPresenter pill, ISoundCues cues, ISettingsService settings,
        IVocabularyService vocabulary, IKeyStateProbe keys, IMediator mediator, TimeProvider time)
    {
        _machine = machine;
        _matcher = matcher;
        _recorder = recorder;
        _engine = engine;
        _postProcessors = [.. postProcessors.OrderBy(p => p.Order)];
        _inserter = inserter;
        _history = history;
        _audioArchive = audioArchive;
        _pill = pill;
        _cues = cues;
        _settingsService = settings;
        _vocabulary = vocabulary;
        _keys = keys;
        _mediator = mediator;
        _time = time;
        _context = SynchronizationContext.Current;

        _recorder.SamplesAvailable += OnSamplesAvailable;
        _recorder.CaptureFailed += OnCaptureFailed;
    }

    /// <summary>The in-flight stop/transcribe/insert, or <see cref="Task.CompletedTask"/>. It never faults.</summary>
    public Task CurrentWork { get; private set; } = Task.CompletedTask;

    /// <summary>Feeds one hotkey signal to the state machine and carries out the result. Call on the UI thread.</summary>
    public void HandleSignal(HotkeySignal signal)
    {
        if (_disposed)
            return;

        var now = _time.GetUtcNow();
        Execute(signal switch
        {
            HotkeySignal.HotkeyDown => _machine.OnHotkeyDown(now, _engine.Status.State == EngineState.Ready),
            HotkeySignal.HotkeyUp => _machine.OnHotkeyUp(now),
            HotkeySignal.OtherInputWhileHeld => _machine.OnOtherInputWhileHeld(),
            HotkeySignal.Escape => _machine.OnEscape(),
            _ => DictationAction.None,
        });
    }

    /// <summary>Releases the microphone and drops any dictation in progress. Call on the UI thread.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _recorder.SamplesAvailable -= OnSamplesAvailable;
        _recorder.CaptureFailed -= OnCaptureFailed;
        Abandon();
    }

    private void Execute(DictationAction action)
    {
        switch (action)
        {
            case DictationAction.StartRecording:
                StartRecording();
                break;
            case DictationAction.StopAndPaste:
                StopRecording(paste: true);
                break;
            case DictationAction.StopAndSaveOnly:
                StopRecording(paste: false);
                break;
            case DictationAction.Cancel:
                Abandon();
                _pill.Hide();
                break;
            case DictationAction.NotifyNotReady:
                _pill.ShowNotice(LoadingModelNotice);
                break;
        }
    }

    private void StartRecording()
    {
        _settings = _settingsService.Load();
        var generation = Interlocked.Increment(ref _generation);
        _detector.Reset();
        _micLive = false;
        _recording = true; // set first: without a synchronization context, chunks arrive even from inside Start

        try
        {
            _recorder.Start(_settings.MicrophoneId);
        }
        catch (AudioDeviceException ex)
        {
            _logger.Warning(ex, "Could not start recording");
            _recording = false;
            _machine.Reset();
            _pill.ShowNotice(MicrophoneUnavailableNotice);
            return;
        }

        _pill.ShowListening();
        _matcher.DictationActive = true;
        _timer = _time.CreateTimer(_ => Dispatch(() => OnTick(generation)), null, TickInterval, TickInterval);
    }

    /// <summary>Capture thread, inside the recorder's lock: hand the chunk over and return at once.</summary>
    private void OnSamplesAvailable(float[] chunk)
    {
        var generation = Volatile.Read(ref _generation);
        Dispatch(() => OnChunk(generation, chunk));
    }

    private void OnChunk(int generation, float[] chunk)
    {
        if (generation != _generation || !_recording)
            return;

        if (!_micLive)
        {
            // The first audio marks the moment the microphone is live (a Bluetooth headset can take a second to
            // open), so the start cue tells the owner when to talk.
            _micLive = true;
            if (_settings.SoundCues)
                _cues.PlayStart();
        }

        if (!_detector.SpeechDetected)
        {
            _detector.Process(chunk);
            if (_detector.SpeechDetected)
                _machine.OnSpeechDetected();
        }

        _pill.PushLevel(SpeechDetector.RmsDbfs(chunk));
    }

    private void OnTick(int generation)
    {
        if (generation != _generation || _timer is null)
            return;

        // A swallowed combo key never reaches GetAsyncKeyState, so for a combo the matcher's view is all there is.
        var hotkey = _matcher.Hotkey;
        var physicallyDown = hotkey.IsModifierOnly ? _keys.IsKeyDown(hotkey.VirtualKey) : _matcher.IsHotkeyHeld;

        // The release was missed; forget it, or the next press would be dropped as auto-repeat.
        if (!physicallyDown && _matcher.IsHotkeyHeld)
            _matcher.ResetHeld();

        Execute(_machine.OnTick(_time.GetUtcNow(), physicallyDown));
    }

    private void OnCaptureFailed(Exception error)
    {
        var generation = Volatile.Read(ref _generation);
        Dispatch(() => HandleCaptureFailed(generation, error));
    }

    private void HandleCaptureFailed(int generation, Exception error)
    {
        if (generation != _generation || !_recording)
        {
            _logger.Warning(error, "The microphone failed while not recording");
            return;
        }

        _logger.Warning(error, "The microphone failed during a recording; the dictation was cancelled");

        // The recorder has already dropped the recording and released the device, so there is nothing to cancel
        // (and this may be running inside its callback).
        _recording = false;
        Abandon();
        _machine.Reset();
        _pill.ShowNotice(MicrophoneUnavailableNotice);
    }

    private void StopRecording(bool paste)
    {
        _recording = false;
        var samples = _recorder.Stop();
        if (_settings.SoundCues)
            _cues.PlayStop();

        var cts = new CancellationTokenSource();
        _cts = cts;
        CurrentWork = ProcessRecordingAsync(_generation, samples, _detector.SpeechDetected, paste, cts);
    }

    private async Task ProcessRecordingAsync(
        int generation, float[] samples, bool speechDetected, bool paste, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        try
        {
            var text = speechDetected ? await TranscribeAsync(samples, ct) : "";
            if (!IsCurrent(generation))
                return;

            if (text.Length == 0)
            {
                _pill.Hide(); // silence: paste nothing, save nothing
            }
            else if (paste)
            {
                await PasteAsync(generation, text, samples, ct);
            }
            else
            {
                await SaveAsync(text, "", pasted: false, samples);
                _pill.ShowNotice(SavedNotice);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelled with Esc: the result is discarded.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Transcription failed");
            if (IsCurrent(generation))
                _pill.ShowNotice(TranscriptionFailedNotice);
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
                _cts = null;
            cts.Dispose();

            if (IsCurrent(generation))
            {
                _machine.OnTranscriptionFinished();
                EndDictation();
            }
        }
    }

    /// <returns>The post-processed transcript, or "" when there was no speech in it.</returns>
    private async Task<string> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        _pill.ShowTranscribing();
        var prompt = PromptBuilder.Build(_vocabulary.Load().HintWords);
        var result = await _engine.TranscribeAsync(samples, prompt, ct);
        if (result.MaxNoSpeechProbability >= NoSpeechThreshold)
            return "";

        var text = result.Text;
        foreach (var postProcessor in _postProcessors)
            text = postProcessor.Process(text);

        return string.IsNullOrWhiteSpace(text) ? "" : text;
    }

    private async Task PasteAsync(int generation, string text, float[] samples, CancellationToken ct)
    {
        InsertResult inserted;
        try
        {
            // Off the UI thread: the low-level keyboard hook runs there, and clipboard retries could hold it past
            // LowLevelHooksTimeout, after which Windows silently removes the hook.
            inserted = await Task.Run(() => _inserter.InsertAsync(text, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.Error(ex, "Could not paste the transcript; it is kept in history");
            if (!IsCurrent(generation))
                return;

            await SaveAsync(text, "", pasted: false, samples);
            _pill.ShowNotice(SavedNotice);
            return;
        }

        if (!IsCurrent(generation))
            return;

        await SaveAsync(text, inserted.TargetApp, inserted.Outcome == InsertOutcome.Pasted, samples);
        if (inserted.Outcome == InsertOutcome.LeftOnClipboard)
            _pill.ShowNotice(CopiedNotice);
        else
            _pill.Hide();
    }

    /// <summary>
    /// Records the dictation in history (with its audio, when kept) and announces it. Failures are logged, never
    /// thrown: any paste has already happened and the pill carries on as normal.
    /// </summary>
    private async Task SaveAsync(string text, string targetApp, bool pasted, float[] samples)
    {
        HistoryEntry entry;
        try
        {
            entry = _history.Add(text, targetApp, pasted);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not save the dictation to history");
            return;
        }

        if (_settings.KeepAudio)
        {
            try
            {
                _audioArchive.Save(entry.Id, samples);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the audio of history entry {EntryId}", entry.Id);
            }
        }

        try
        {
            await _mediator.Publish(new HistoryEntryAddedNotification(entry));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not announce history entry {EntryId}", entry.Id);
        }
    }

    /// <summary>
    /// Drops the current dictation: releases the microphone, cancels any transcription or paste in flight (its
    /// result is discarded) and stops polling. The caller updates the pill.
    /// </summary>
    private void Abandon()
    {
        Interlocked.Increment(ref _generation);

        if (_recording)
        {
            _recording = false;
            _recorder.Cancel();
        }

        _cts?.Cancel();
        _cts = null;
        EndDictation();
    }

    private void EndDictation()
    {
        _matcher.DictationActive = false;
        _timer?.Dispose();
        _timer = null;
    }

    private bool IsCurrent(int generation) => generation == Volatile.Read(ref _generation);

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. Always Post, never Send: recorder events are raised inside the
    /// recorder's lock, and a synchronous hop to a UI thread that is calling Stop would deadlock.
    /// </summary>
    private void Dispatch(Action action)
    {
        if (_context is null)
            action();
        else
            _context.Post(static state => ((Action)state!).Invoke(), action);
    }
}
