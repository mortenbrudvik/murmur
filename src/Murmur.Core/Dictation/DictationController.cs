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
using Serilog.Events;

namespace Murmur.Core.Dictation;

/// <summary>
/// Runs dictation end to end. Hotkey signals drive the <see cref="DictationStateMachine"/>; its actions start and
/// stop the microphone, transcribe, post-process, paste and record history, with the pill and sound cues as
/// feedback.
/// <para>
/// Single-threaded by design: everything runs on the thread that created the controller (the UI thread). Recorder
/// events and timer ticks are posted to that thread's <see cref="SynchronizationContext"/>, or run inline when it
/// has none (tests). Only opening the microphone (it can take a second) and the clipboard work (paste, copy) run on
/// the thread pool; without a synchronization context the microphone opens inline too.
/// </para>
/// </summary>
public sealed class DictationController : IDisposable
{
    private const string LoadingModelNotice = "Loading speech model…";
    private const string DownloadingModelNotice = "Downloading speech model…";
    private const string EngineUnavailableNotice = "Speech engine unavailable — open Murmur";
    private const string CopiedNotice = "Copied — press Ctrl+V";
    private const string SavedNotice = "Saved to history";
    private const string MicrophoneUnavailableNotice = "Microphone unavailable";
    private const string TranscriptionFailedNotice = "Transcription failed";

    // How a dictation ended, for the one log line each dictation gets.
    private const string PastedOutcome = "pasted";
    private const string LeftOnClipboardOutcome = "left-on-clipboard";
    private const string SavedOnlyOutcome = "saved-only";
    private const string SilenceOutcome = "silence";
    private const string CancelledOutcome = "cancelled";
    private const string FailedOutcome = "failed";

    /// <summary>
    /// Whisper's own silence verdict. The speech detector is the real gate (Whisper.net reports 0 here); this is a
    /// harmless second guard.
    /// </summary>
    private const float NoSpeechThreshold = 0.6f;

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// While armed, how often the tick polls once the arm delay is up. A timer can fire up to a clock tick (15.6 ms) early
    /// by the clock the press is timed with, so the first poll can come just before the delay is over; waiting a whole
    /// <see cref="TickInterval"/> for the next one would hold the start back by another 100 ms.
    /// </summary>
    private static readonly TimeSpan ArmPollInterval = TimeSpan.FromMilliseconds(10);

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

    // Identifies the current dictation. Bumped when one arms, when it starts and when it is abandoned (Esc, microphone
    // failure), so recorder events, ticks and completions that belong to an older one recognise themselves as stale.
    private int _generation;

    // The dictation the recorder's recording belongs to: set before each open, read on the capture thread to tag its
    // chunks and failures. Not _generation itself: after Esc and a quick re-press, the first microphone still delivers
    // audio until its open finishes and it is cancelled, while the second press is already the current dictation.
    private int _captureGeneration;

    private MurmurSettings _settings = MurmurSettings.Default; // snapshot taken when a recording starts
    private DictationStats _stats = new(default); // the current (or last) recording's numbers, for its log line
    private bool _recording;
    private bool _micLive;

    // The microphone open in flight (Start on the thread pool, then the rest back on this thread), or a completed task.
    // Never meant to fault. The next open waits for it, because the recorder takes one recording at a time, and so does a
    // stop; both through WaitForOpenAsync, so that a fault (a bug) cannot stop every later dictation.
    private Task _opening = Task.CompletedTask;

    // The recorder's Start of the latest open, on the thread pool. Dispose releases the microphone from there.
    private Task _starting = Task.CompletedTask;

    // An open has finished and its recording is still running, so it needs a Stop or Cancel. Never set while an open is
    // in flight: Stop and Cancel wait for the open to finish, which would block the UI thread.
    private bool _microphoneOpen;

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
        if (_context is null)
            _logger.Warning("Created without a SynchronizationContext: recorder events and ticks will run on other threads");

        _recorder.SamplesAvailable += OnSamplesAvailable;
        _recorder.CaptureFailed += OnCaptureFailed;
    }

    /// <summary>The in-flight stop/transcribe/insert, or <see cref="Task.CompletedTask"/>. It never faults.</summary>
    public Task CurrentWork { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The microphone open in flight, or a completed task. It finishes on the UI thread and is not meant to fault. Tests
    /// wait for it.
    /// </summary>
    internal Task MicrophoneOpening => _opening;

    /// <summary>Feeds one hotkey signal to the state machine and carries out the result. Call on the UI thread.</summary>
    /// <param name="at">
    /// When the keyboard hook saw the key. Tap and hold are timed from this, not from when the UI thread gets round to
    /// the signal, which can be later when the UI thread is busy.
    /// </param>
    public void HandleSignal(HotkeySignal signal, DateTimeOffset at)
    {
        if (_disposed)
            return;

        // One snapshot per signal, so the not-ready notice describes the state the decision was made on. Dictation needs
        // a loaded model, not a Ready status: while a switched-to model loads, the previous one keeps transcribing.
        var status = _engine.Status;
        var action = signal switch
        {
            HotkeySignal.HotkeyDown => _machine.OnHotkeyDown(at, _engine.CanTranscribe, _matcher.Hotkey.IsModifierOnly),
            HotkeySignal.HotkeyUp => _machine.OnHotkeyUp(at),
            HotkeySignal.OtherInputWhileHeld => _machine.OnOtherInputWhileHeld(),
            HotkeySignal.Escape => _machine.OnEscape(),
            _ => DictationAction.None,
        };

        if (action == DictationAction.NotifyNotReady)
            _pill.ShowNotice(DescribeNotReady(status));
        else
            Execute(action, at);
    }

    /// <summary>
    /// Why the hotkey does nothing, from the engine's state. The pill is click-through, so the text can only say what is
    /// going on, or send the user to Murmur's window for a failure.
    /// </summary>
    private static string DescribeNotReady(EngineStatus status) => status.State switch
    {
        EngineState.Downloading when status.Download is { TotalBytes: > 0 } download =>
            $"{DownloadingModelNotice} {Math.Clamp(download.BytesReceived * 100 / download.TotalBytes, 0, 100)}%",
        EngineState.Downloading => DownloadingModelNotice,
        EngineState.Error => EngineUnavailableNotice,
        _ => LoadingModelNotice,
    };

    /// <summary>
    /// Releases the microphone (once it has opened, if it is still opening) and drops any dictation in progress. Call on
    /// the UI thread.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _recorder.SamplesAvailable -= OnSamplesAvailable;
        _recorder.CaptureFailed -= OnCaptureFailed;
        Abandon();

        // Still opening: release the microphone from the thread pool when the open finishes. The open itself finishes on
        // this thread, which a closing app stops pumping, and waiting for it here would block this thread.
        if (!_opening.IsCompleted)
        {
            _ = _starting.ContinueWith(
                static (start, recorder) =>
                {
                    _ = start.Exception; // a failed open opened nothing; the open logs the failure if it gets to finish
                    ((IAudioRecorder)recorder!).Cancel();
                },
                _recorder, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <param name="at">When the event that led to <paramref name="action"/> happened.</param>
    private void Execute(DictationAction action, DateTimeOffset at)
    {
        switch (action)
        {
            case DictationAction.Arm:
                Arm();
                break;
            case DictationAction.StartRecording:
                // Timed from the press, also when recording starts only after the arm delay or on a tap's release.
                StartRecording(_machine.PressedAt);
                break;
            case DictationAction.StopAndPaste:
                StopRecording(paste: true, at);
                break;
            case DictationAction.StopAndSaveOnly:
                StopRecording(paste: false, at);
                break;
            case DictationAction.Cancel:
                Abandon();
                _pill.Hide();
                break;
            case DictationAction.None when !_machine.IsActive:
                // An armed press that came to nothing (a shortcut, Esc, or a release only the tick saw) started nothing
                // but its tick. Otherwise there is nothing running while idle, and this does nothing.
                EndDictation();
                break;
        }
    }

    /// <summary>
    /// A lone-modifier hotkey went down. It may be the start of another app's shortcut (Right Ctrl+C), so nothing starts
    /// yet: no pill, no microphone, no cue, and Esc still reaches the app. Only the tick runs, from when the arm delay is
    /// up; another key, a release or the tick then decides.
    /// </summary>
    private void Arm()
    {
        var generation = Interlocked.Increment(ref _generation);
        StartTicking(generation, _machine.Timings.ArmDelay, ArmPollInterval);
    }

    private void StartRecording(DateTimeOffset pressedAt)
    {
        _settings = _settingsService.Load();
        var generation = Interlocked.Increment(ref _generation);
        _detector.Reset();
        _stats = new DictationStats(pressedAt);
        _micLive = false;
        _recording = true; // set first: without a synchronization context, chunks arrive even from inside Start

        // Opening the microphone can take a second (a Bluetooth headset), so it opens on the thread pool and the rest
        // happens at once: the pill shows, and the hook catches Esc and a combo key. If the microphone cannot be opened,
        // a notice replaces the pill.
        _pill.ShowListening();
        _matcher.DictationActive = true;
        _opening = OpenMicrophoneAsync(generation, _settings.MicrophoneId, _opening);

        // Polls while the microphone opens too (a missed release); not when the open has already failed.
        if (IsCurrent(generation))
            StartTicking(generation, TickInterval, TickInterval);
    }

    /// <summary>Starts polling for dictation <paramref name="generation"/>, in place of an armed press's poll.</summary>
    private void StartTicking(int generation, TimeSpan dueTime, TimeSpan period)
    {
        _timer?.Dispose();
        _timer = _time.CreateTimer(_ => Dispatch(() => OnTick(generation)), null, dueTime, period);
    }

    /// <summary>
    /// Opens the microphone for dictation <paramref name="generation"/> on the thread pool and finishes on the UI thread.
    /// What ends a dictation while its microphone opens leaves the microphone to this method (Esc, a combo key, a
    /// capture failure: it cancels the recording once open) or to the stop that waits for it (a release). Not meant to
    /// fault.
    /// </summary>
    /// <param name="previous">
    /// The previous open. The recorder takes one recording at a time, so after Esc and a quick re-press this waits for the
    /// previous open to finish and release its microphone.
    /// </param>
    private async Task OpenMicrophoneAsync(int generation, string? microphoneId, Task previous)
    {
        await WaitForOpenAsync(previous);
        if (!IsCurrent(generation))
            return; // ended before its turn came: nothing to open

        Volatile.Write(ref _captureGeneration, generation);
        var openingAt = _time.GetTimestamp();
        Exception? failure = null;
        try
        {
            _starting = StartRecorder(microphoneId);
            await _starting;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (failure is not null)
        {
            OnOpenFailed(generation, failure);
            return;
        }

        if (_disposed)
            return; // Dispose releases this recording from the thread pool

        _logger.Information(
            "Microphone opened in {ElapsedMs} ms", (long)_time.GetElapsedTime(openingAt).TotalMilliseconds);

        // Still recording, or released meanwhile: then the stop waiting for this open stops it.
        if (IsCurrent(generation))
        {
            _microphoneOpen = true;
            return;
        }

        // Esc or a combo key while it opened, or the microphone failed already (then this is a no-op).
        _recorder.Cancel();
    }

    /// <summary>
    /// Waits for a microphone open to finish. An open is not meant to fault, but every later open and every stop waits for
    /// the one before, so a fault (a bug) would end dictation until Murmur restarts: it is logged here instead of rethrown.
    /// An open that has finished already continues at once.
    /// </summary>
    private async Task WaitForOpenAsync(Task opening)
    {
        try
        {
            await opening;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Opening the microphone failed unexpectedly");
        }
    }

    /// <summary>
    /// Starts the recorder off the UI thread. Without a synchronization context (tests) there is no UI thread to keep
    /// free, and no way back to the controller's thread, so it starts inline.
    /// </summary>
    private Task StartRecorder(string? microphoneId)
    {
        if (_context is not null)
            return Task.Run(() => _recorder.Start(microphoneId));

        _recorder.Start(microphoneId);
        return Task.CompletedTask;
    }

    private void OnOpenFailed(int generation, Exception error)
    {
        // AudioDeviceException is the expected failure (no microphone, device in use); anything else is a bug.
        var level = error is AudioDeviceException ? LogEventLevel.Warning : LogEventLevel.Error;
        if (!IsCurrent(generation))
        {
            _logger.Write(level, error, "Could not open the microphone for a dictation that had already ended");
            return;
        }

        _logger.Write(level, error, "Could not start recording");

        // Released while the microphone opened: the stop waiting for the open reports the failure.
        if (_recording)
            FailRecording();
    }

    /// <summary>Capture thread, inside the recorder's lock: hand the chunk over and return at once.</summary>
    private void OnSamplesAvailable(float[] chunk)
    {
        var generation = Volatile.Read(ref _captureGeneration);
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

        var level = SpeechDetector.RmsDbfs(chunk);
        _stats.PeakDbfs = Math.Max(_stats.PeakDbfs, level);
        _pill.PushLevel(level);
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

        var now = _time.GetUtcNow();
        Execute(_machine.OnTick(now, physicallyDown), now);
    }

    private void OnCaptureFailed(Exception error)
    {
        var generation = Volatile.Read(ref _captureGeneration);
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
        _microphoneOpen = false;
        FailRecording();
    }

    /// <summary>The microphone could not be opened, or failed while recording: drops the dictation and says so.</summary>
    private void FailRecording()
    {
        _recording = false;
        Abandon();
        _machine.Reset();
        _pill.ShowNotice(MicrophoneUnavailableNotice);
        LogRecordingEnded(FailedOutcome);
    }

    private void StopRecording(bool paste, DateTimeOffset releasedAt)
    {
        _recording = false;
        var stats = _stats;
        stats.StoppedAt = releasedAt;

        var cts = new CancellationTokenSource();
        _cts = cts;
        CurrentWork = ProcessRecordingAsync(_generation, _opening, stats, paste, cts);
    }

    /// <param name="opening">
    /// The microphone's open. When the hotkey was released before it finished, the recording is stopped once it has.
    /// </param>
    private async Task ProcessRecordingAsync(
        int generation, Task opening, DictationStats stats, bool paste, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        var outcome = FailedOutcome;
        try
        {
            // Not Stop while the open is running: Stop waits for it, and this is the UI thread. Already open, this
            // continues at once.
            await WaitForOpenAsync(opening);
            if (!IsCurrent(generation))
            {
                outcome = CancelledOutcome; // Esc while it opened: Abandon or the open released the microphone
                return;
            }

            if (!_microphoneOpen)
            {
                _pill.ShowNotice(MicrophoneUnavailableNotice); // the open failed, and logged why
                return;
            }

            _microphoneOpen = false;
            var samples = _recorder.Stop();
            if (_settings.SoundCues)
                _cues.PlayStop();

            stats.AudioMs = samples.Length / (Resampler16k.TargetRate / 1000);
            stats.SpeechDetected = _detector.SpeechDetected;

            var text = stats.SpeechDetected ? await TranscribeAsync(samples, ct) : "";
            stats.Characters = text.Length;
            if (!IsCurrent(generation))
            {
                outcome = CancelledOutcome; // Esc during the transcription: the result is discarded
                return;
            }

            if (text.Length == 0)
            {
                outcome = SilenceOutcome;
                _pill.Hide(); // paste nothing, save nothing
            }
            else if (paste)
            {
                outcome = await PasteAsync(generation, text, samples, stats, ct);
            }
            else
            {
                outcome = await KeepUnpastedAsync(generation, text, samples);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = CancelledOutcome; // Esc: the result is discarded
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

            LogOutcome(outcome, stats);
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

        // Trimmed last: a vocabulary rule that deletes a leading or trailing word ("um" → "") leaves a space behind.
        return text.Trim();
    }

    /// <returns>The outcome for the dictation's log line.</returns>
    private async Task<string> PasteAsync(
        int generation, string text, float[] samples, DictationStats stats, CancellationToken ct)
    {
        InsertResult inserted;
        try
        {
            // Off the UI thread: clipboard retries and the wait for modifiers to be released would freeze it.
            inserted = await Task.Run(() => _inserter.InsertAsync(text, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.Error(ex, "Could not paste the transcript");
            return IsCurrent(generation) ? await KeepUnpastedAsync(generation, text, samples) : CancelledOutcome;
        }

        // The text has left Murmur (it is in the target app, or on the clipboard), so it goes into history even if Esc
        // was pressed in the meantime; only the pill is left alone then.
        stats.ReleaseToPasteMs = (long)(_time.GetUtcNow() - stats.StoppedAt).TotalMilliseconds;
        stats.TargetApp = inserted.TargetApp;
        var pasted = inserted.Outcome == InsertOutcome.Pasted;

        // A failed save is only logged: the text is already where the owner wanted it.
        await SaveAsync(text, inserted.TargetApp, pasted, samples);
        if (IsCurrent(generation))
        {
            if (pasted)
                _pill.Hide();
            else
                _pill.ShowNotice(CopiedNotice);
        }

        return pasted ? PastedOutcome : LeftOnClipboardOutcome;
    }

    /// <summary>
    /// For text that was not pasted (a hands-free session that hit its time limit, or a failed paste): keeps it in
    /// history, or when that fails, on the clipboard.
    /// </summary>
    /// <returns>The outcome for the dictation's log line.</returns>
    private async Task<string> KeepUnpastedAsync(int generation, string text, float[] samples)
    {
        if (await SaveAsync(text, "", pasted: false, samples))
        {
            if (IsCurrent(generation))
                _pill.ShowNotice(SavedNotice);
            return SavedOnlyOutcome;
        }

        if (!IsCurrent(generation))
            return CancelledOutcome;

        // Off the UI thread: the copy waits for any clipboard restore still pending.
        var copied = await Task.Run(() => _inserter.TryCopy(text));
        if (IsCurrent(generation))
            _pill.ShowNotice(copied ? CopiedNotice : TranscriptionFailedNotice);

        return copied ? LeftOnClipboardOutcome : FailedOutcome;
    }

    /// <summary>Records the dictation in history (with its audio, when kept) and announces it. Failures are logged, never thrown.</summary>
    /// <returns>Whether the entry is in history; losing only its audio or its announcement still counts as saved.</returns>
    private async Task<bool> SaveAsync(string text, string targetApp, bool pasted, float[] samples)
    {
        HistoryEntry entry;
        try
        {
            entry = _history.Add(text, targetApp, pasted);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not save the dictation to history");
            return false;
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

        return true;
    }

    /// <summary>
    /// Drops the current dictation: releases the microphone, cancels any transcription or paste in flight (its
    /// result is discarded, unless Ctrl+V was already sent) and stops polling. The caller updates the pill.
    /// A microphone that is still opening is cancelled by its open once it finishes.
    /// </summary>
    private void Abandon()
    {
        Interlocked.Increment(ref _generation);

        if (_microphoneOpen)
        {
            _microphoneOpen = false;
            _recorder.Cancel();
        }

        // Once released, the stop that took the recording over writes its log line.
        if (_recording)
        {
            _recording = false;
            LogRecordingEnded(CancelledOutcome);
        }

        _cts?.Cancel();
        _cts = null;
        EndDictation();
    }

    /// <summary>Logs the outcome of a dictation that ended while still recording (cancelled, or the microphone failed).</summary>
    private void LogRecordingEnded(string outcome)
    {
        _stats.StoppedAt = _time.GetUtcNow();
        _stats.SpeechDetected = _detector.SpeechDetected;
        LogOutcome(outcome, _stats);
    }

    /// <summary>The one Information line every dictation ends with. Never includes the transcript, only its length.</summary>
    private void LogOutcome(string outcome, DictationStats stats) =>
        _logger.Information(
            "Dictation {Outcome:l}: recorded {RecordingMs} ms ({AudioMs} ms of audio), speech detected {SpeechDetected}, " +
            "peak {PeakDbfs:F1} dBFS, {Characters} characters, release to paste {ReleaseToPasteMs} ms, target {TargetApp}",
            outcome, (long)(stats.StoppedAt - stats.StartedAt).TotalMilliseconds, stats.AudioMs, stats.SpeechDetected,
            stats.PeakDbfs, stats.Characters, stats.ReleaseToPasteMs, stats.TargetApp);

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

    /// <summary>What a dictation's log line reports. Holds no transcript text.</summary>
    /// <param name="startedAt">When the hotkey was pressed.</param>
    private sealed class DictationStats(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;

        /// <summary>When the recording stopped (hotkey released) or was abandoned.</summary>
        public DateTimeOffset StoppedAt { get; set; } = startedAt;

        public int AudioMs { get; set; }

        public bool SpeechDetected { get; set; }

        /// <summary>The loudest captured chunk's RMS level; the meter's floor until audio arrives.</summary>
        public float PeakDbfs { get; set; } = -120f;

        public int Characters { get; set; }

        /// <summary>From the release to Ctrl+V being sent (or the text left on the clipboard); null when nothing was pasted.</summary>
        public long? ReleaseToPasteMs { get; set; }

        public string TargetApp { get; set; } = "";
    }
}
