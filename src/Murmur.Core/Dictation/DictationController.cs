using Murmur.Core.Audio;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Serilog;
using Serilog.Events;

namespace Murmur.Core.Dictation;

/// <summary>
/// Runs dictation end to end. Hotkey signals drive the <see cref="DictationStateMachine"/>; its actions start and
/// stop the microphone and hand the recording to <see cref="DictationDelivery"/> (transcribe, paste, record history),
/// with the pill, sound cues and Windows notifications as feedback.
/// <para>
/// Single-threaded by design: everything runs on the thread that created the controller (the UI thread). Recorder
/// events and timer ticks are posted to that thread's <see cref="SynchronizationContext"/>, or run inline when it
/// has none (tests). Only opening the microphone (it can take a second) and, in <see cref="DictationDelivery"/>, the
/// clipboard work (paste, copy) and the history and audio file writes run on the thread pool; without a synchronization
/// context the microphone opens inline too.
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
    private const string NothingHeardNotice = "Didn't hear anything";

    // A recording at least this long that held no speech was meant to be one, so the user is told nothing was heard
    // (a muted or wrong microphone). A shorter one is taken as an accidental tap and ends silently.
    private const int NothingHeardMinAudioMs = 1000;

    // The Windows notifications for text that was not pasted: the pill says it too, but is gone after a few seconds.
    private static readonly UnpastedNotice CopiedUnpasted = new(
        CopiedNotice, "Murmur couldn't paste your text", "It's on the clipboard. Press Ctrl+V to paste it.");
    private static readonly UnpastedNotice SavedUnpasted = new(
        SavedNotice, "Murmur saved your text to History", "It wasn't pasted. Open Murmur to copy it from History.");
    private static readonly UnpastedNotice LostUnpasted = new(
        TranscriptionFailedNotice, "Murmur couldn't keep your text",
        "It couldn't be pasted, saved to History or copied. Murmur's log has the details.");

    // Generic virtual-key codes, which cover both sides of the keyboard; the Win keys have none.
    private const int VkShift = 0x10;
    private const int VkCtrl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkLeftWin = 0x5B;
    private const int VkRightWin = 0x5C;

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// While armed, how often the tick polls once the arm delay is up. A timer can fire up to a clock tick (15.6 ms) early
    /// by the clock the press is timed with, so the first poll can come just before the delay is over; waiting a whole
    /// <see cref="TickInterval"/> for the next one would hold the start back by another 100 ms.
    /// </summary>
    private static readonly TimeSpan ArmPollInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// How much audio makes one bar of the pill's waveform: 800 samples, 50 ms at 16 kHz. The capture buffers are about
    /// 10 ms, which is far too fast for a bar. Counted in samples, not by a clock, so it follows the audio.
    /// </summary>
    private const int LevelWindowSamples = 800;

    private readonly ILogger _logger = Log.ForContext<DictationController>();
    private readonly DictationStateMachine _machine;
    private readonly HotkeyMatcher _matcher;
    private readonly IAudioRecorder _recorder;
    private readonly ISpeechEngine _engine;
    private readonly DictationDelivery _delivery;
    private readonly IPillPresenter _pill;
    private readonly ISoundCues _cues;
    private readonly IUserNotifier _notifier;
    private readonly ISettingsService _settingsService;
    private readonly IKeyStateProbe _keys;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _context;
    private readonly SpeechDetector _detector = new();

    // The audio of the bar being gathered, and how much of it is in. UI thread only, like everything that handles chunks.
    private readonly float[] _levelWindow = new float[LevelWindowSamples];
    private int _levelWindowFill;

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

    /// <param name="engine">Asked whether it can transcribe; <paramref name="delivery"/> does the transcribing.</param>
    public DictationController(
        DictationStateMachine machine, HotkeyMatcher matcher, IAudioRecorder recorder, ISpeechEngine engine,
        DictationDelivery delivery, IPillPresenter pill, ISoundCues cues, IUserNotifier notifier,
        ISettingsService settings, IKeyStateProbe keys, TimeProvider time)
    {
        _machine = machine;
        _matcher = matcher;
        _recorder = recorder;
        _engine = engine;
        _delivery = delivery;
        _pill = pill;
        _cues = cues;
        _notifier = notifier;
        _settingsService = settings;
        _keys = keys;
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
        var ready = _engine.CanTranscribe;
        var action = signal switch
        {
            HotkeySignal.HotkeyDown => _machine.OnHotkeyDown(at, ready, _matcher.Hotkey.IsModifierOnly),
            HotkeySignal.HotkeyUp => _machine.OnHotkeyUp(at, ready),
            HotkeySignal.OtherInputWhileHeld => _machine.OnOtherInputWhileHeld(),
            HotkeySignal.Escape => _machine.OnEscape(),
            _ => DictationAction.None,
        };

        Execute(action, at, status);
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
    /// <param name="status">The engine's state when <paramref name="action"/> was decided, for the not-ready notice.</param>
    private void Execute(DictationAction action, DateTimeOffset at, EngineStatus status)
    {
        switch (action)
        {
            case DictationAction.NotifyNotReady:
                // A combo press, or an armed press that turned out to be a dictation: nothing starts, and an armed press's
                // tick stops.
                NotifyNotReady(status);
                EndDictation();
                break;
            case DictationAction.Arm:
                Arm(_machine.PressedAt);
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
    /// <param name="pressedAt">
    /// When the hook saw the press. The arm delay runs from then, not from now: the UI thread may get to the press late.
    /// </param>
    private void Arm(DateTimeOffset pressedAt)
    {
        var generation = Interlocked.Increment(ref _generation);
        var armDelay = _machine.Timings.ArmDelay;
        var left = armDelay - (_time.GetUtcNow() - pressedAt);

        // Never more than the whole delay: a press stamped ahead of this clock would otherwise move the first poll out.
        var dueTime = left < TimeSpan.Zero ? TimeSpan.Zero : left > armDelay ? armDelay : left;
        StartTicking(generation, dueTime, ArmPollInterval);
    }

    private void StartRecording(DateTimeOffset pressedAt)
    {
        RefreshSettings();
        var generation = Interlocked.Increment(ref _generation);
        _detector.Reset();
        _levelWindowFill = 0;
        _stats = new DictationStats(pressedAt);
        _micLive = false;
        _recording = true; // set first: without a synchronization context, chunks arrive even from inside Start

        // Opening the microphone can take a second (a Bluetooth headset), so it opens on the thread pool and the rest
        // happens at once: the pill shows, and the hook catches Esc and a combo key. The pill shows that the microphone
        // is starting, not that it is listening: audio comes only some time after the open (most of a second on some
        // devices), and what is said before then is lost. The first audio turns it to listening. If the microphone
        // cannot be opened, a notice replaces the pill.
        _pill.ShowStarting();
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
            // open), so the pill and the start cue tell the owner when to talk.
            _micLive = true;
            _stats.FirstAudioMs = (long)(_time.GetUtcNow() - _stats.StartedAt).TotalMilliseconds;
            _pill.ShowListening();
            if (_settings.SoundCues)
                _cues.PlayStart();
        }

        if (!_detector.SpeechDetected)
        {
            _detector.Process(chunk);
            if (_detector.SpeechDetected)
                _machine.OnSpeechDetected();
        }

        _stats.PeakDbfs = Math.Max(_stats.PeakDbfs, SpeechDetector.RmsDbfs(chunk));
        PushLevels(chunk);
    }

    /// <summary>
    /// Gathers the audio into windows of <see cref="LevelWindowSamples"/> and gives the pill the RMS level of each full
    /// one. A window may span chunks and a chunk may hold several windows; what is left over when the recording ends is
    /// never pushed.
    /// </summary>
    private void PushLevels(ReadOnlySpan<float> chunk)
    {
        while (!chunk.IsEmpty)
        {
            var count = Math.Min(chunk.Length, LevelWindowSamples - _levelWindowFill);
            chunk[..count].CopyTo(_levelWindow.AsSpan(_levelWindowFill));
            _levelWindowFill += count;
            chunk = chunk[count..];

            if (_levelWindowFill == LevelWindowSamples)
            {
                _levelWindowFill = 0;
                _pill.PushLevel(SpeechDetector.RmsDbfs(_levelWindow));
            }
        }
    }

    private void OnTick(int generation)
    {
        if (generation != _generation || _timer is null)
            return;

        var hotkey = _matcher.Hotkey;
        var physicallyDown = IsHotkeyPhysicallyDown(hotkey);

        if (!physicallyDown && _matcher.IsHotkeyHeld)
        {
            // A lone modifier's release was missed: forget it, or the next press would be dropped as auto-repeat. A combo
            // whose modifiers are all up may still have its main key down and auto-repeating, which must stay swallowed
            // until its key-up; the matcher only learns that the combo pressed again is a new press.
            if (hotkey.IsModifierOnly)
                _matcher.ResetHeld();
            else
                _matcher.NoteModifiersReleased();
        }

        // An armed press held past the arm delay is decided on the engine as it is now, like a signal.
        var now = _time.GetUtcNow();
        var status = _engine.Status;
        var holding = _machine.State == DictationState.HoldRecording;
        var action = _machine.OnTick(now, physicallyDown, _engine.CanTranscribe);

        // A hold that ran into its limit has no release to forget it; without this the next press would be dropped as
        // auto-repeat. (A hands-free session ending on its limit is not a held key.)
        if (holding && action == DictationAction.StopAndSaveOnly)
            _matcher.ResetHeld();

        Execute(action, now, status);
    }

    /// <summary>
    /// Whether the hotkey is still held, as far as can be told. A lone modifier is never swallowed, so the key-state probe
    /// reads it. A combo's main key is swallowed and never reaches the probe, so the matcher's view of it is all there is;
    /// its modifiers are not swallowed, though, and with all of them up the combo cannot be held, whatever key-up the hook
    /// missed. An F-key hotkey has no modifier to read, so only the hold limit ends a missed release of it.
    /// </summary>
    private bool IsHotkeyPhysicallyDown(HotkeyDefinition hotkey)
    {
        if (hotkey.IsModifierOnly)
            return _keys.IsKeyDown(hotkey.VirtualKey);

        if (!_matcher.IsHotkeyHeld)
            return false;

        return hotkey.Modifiers == HotkeyModifiers.None || IsAnyModifierDown(hotkey.Modifiers);
    }

    /// <summary>Whether any of <paramref name="modifiers"/> is down, on either side of the keyboard.</summary>
    private bool IsAnyModifierDown(HotkeyModifiers modifiers) =>
        (modifiers.HasFlag(HotkeyModifiers.Ctrl) && _keys.IsKeyDown(VkCtrl))
        || (modifiers.HasFlag(HotkeyModifiers.Alt) && _keys.IsKeyDown(VkAlt))
        || (modifiers.HasFlag(HotkeyModifiers.Shift) && _keys.IsKeyDown(VkShift))
        || (modifiers.HasFlag(HotkeyModifiers.Win) && (_keys.IsKeyDown(VkLeftWin) || _keys.IsKeyDown(VkRightWin)));

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
        ShowProblem(MicrophoneUnavailableNotice);
        LogRecordingEnded(DictationOutcome.Failed);
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
        var outcome = DictationOutcome.Failed;
        try
        {
            // Not Stop while the open is running: Stop waits for it, and this is the UI thread. Already open, this
            // continues at once.
            await WaitForOpenAsync(opening);
            if (!IsCurrent(generation))
            {
                outcome = DictationOutcome.Cancelled; // Esc while it opened: Abandon or the open released the microphone
                return;
            }

            if (!_microphoneOpen)
            {
                ShowProblem(MicrophoneUnavailableNotice); // the open failed, and logged why
                return;
            }

            _microphoneOpen = false;
            var samples = _recorder.Stop();
            if (_settings.SoundCues)
                _cues.PlayStop();

            stats.AudioMs = samples.Length / (Resampler16k.TargetRate / 1000);
            stats.SpeechDetected = _detector.SpeechDetected;

            var text = "";
            if (stats.SpeechDetected)
            {
                _pill.ShowTranscribing();
                text = await _delivery.TranscribeAsync(samples, ct);
            }

            stats.Characters = text.Length;
            if (!IsCurrent(generation))
            {
                outcome = DictationOutcome.Cancelled; // Esc during the transcription: the result is discarded
                return;
            }

            if (text.Length == 0)
            {
                // Paste nothing, save nothing. A tap that was too short to say anything ends silently.
                outcome = DictationOutcome.Silence;
                if (stats.AudioMs >= NothingHeardMinAudioMs)
                    ShowProblem(NothingHeardNotice);
                else
                    _pill.Hide();
            }
            else
            {
                outcome = await DeliverAsync(generation, text, samples, stats, paste, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = DictationOutcome.Cancelled; // Esc: the result is discarded
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Transcription failed");
            if (IsCurrent(generation))
                ShowProblem(TranscriptionFailedNotice);
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

    /// <summary>
    /// Pastes the text, or only keeps it when it is not to be pasted, and tells the owner where it went unless the
    /// dictation was cancelled meanwhile.
    /// </summary>
    /// <returns>The outcome for the dictation's log line.</returns>
    private async Task<DictationOutcome> DeliverAsync(
        int generation, string text, float[] samples, DictationStats stats, bool paste, CancellationToken ct)
    {
        var keepAudio = _settings.KeepAudio;
        DictationOutcome outcome;
        if (paste)
        {
            var delivered = await _delivery.PasteAsync(text, samples, keepAudio, () => IsCurrent(generation), ct);
            outcome = delivered.Outcome;
            stats.TargetApp = delivered.TargetApp;
            if (delivered.InsertedAt is { } insertedAt)
                stats.ReleaseToPasteMs = (long)(insertedAt - stats.StoppedAt).TotalMilliseconds;
        }
        else
        {
            outcome = await _delivery.KeepUnpastedAsync(text, samples, keepAudio, () => IsCurrent(generation));
        }

        // Text that reached the target app or history after Esc is not reported: the pill is left alone then.
        if (IsCurrent(generation))
            ShowDelivered(outcome);

        return outcome;
    }

    /// <summary>
    /// Says where delivered text went. Pasted text needs no word, so the pill just goes; text that was not pasted is a
    /// problem, told in the pill and in a Windows notification.
    /// </summary>
    private void ShowDelivered(DictationOutcome outcome)
    {
        switch (outcome)
        {
            case DictationOutcome.Pasted:
                _pill.Hide();
                break;
            case DictationOutcome.LeftOnClipboard:
                NotifyUnpasted(CopiedUnpasted);
                break;
            case DictationOutcome.SavedOnly:
                NotifyUnpasted(SavedUnpasted);
                break;
            case DictationOutcome.Failed:
                NotifyUnpasted(LostUnpasted);
                break;
        }
    }

    /// <summary>
    /// The hotkey was pressed while the engine cannot transcribe. A model that is still loading or downloading is passing
    /// news; an engine that failed is a problem.
    /// </summary>
    private void NotifyNotReady(EngineStatus status)
    {
        if (status.State != EngineState.Error)
        {
            _pill.ShowNotice(DescribeNotReady(status), NoticeKind.Info);
            return;
        }

        // No recording started, so the settings snapshot may be older than the owner's last change to the sound cues.
        RefreshSettings();
        ShowProblem(DescribeNotReady(status));
    }

    /// <summary>
    /// Takes a new settings snapshot. When the settings cannot be read (the file is locked, or access to it is denied),
    /// the previous snapshot stays: a dictation that has started must not fail on its settings.
    /// </summary>
    private void RefreshSettings()
    {
        try
        {
            _settings = _settingsService.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(ex, "Could not read the settings; using the ones read before");
        }
    }

    /// <summary>Shows a problem in the pill, with the problem cue when sound cues are on.</summary>
    private void ShowProblem(string notice)
    {
        _pill.ShowNotice(notice, NoticeKind.Problem);
        if (_settings.SoundCues)
            _cues.PlayProblem();
    }

    /// <summary>
    /// Text that was not pasted: says so in the pill and in a Windows notification, which stays in the notification
    /// centre after the pill is gone (the pill is click-through and nothing else repeats it).
    /// </summary>
    private void NotifyUnpasted(UnpastedNotice notice)
    {
        ShowProblem(notice.Pill);
        _notifier.Notify(notice.Title, notice.Message);
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
            LogRecordingEnded(DictationOutcome.Cancelled);
        }

        _cts?.Cancel();
        _cts = null;
        EndDictation();
    }

    /// <summary>Logs the outcome of a dictation that ended while still recording (cancelled, or the microphone failed).</summary>
    private void LogRecordingEnded(DictationOutcome outcome)
    {
        _stats.StoppedAt = _time.GetUtcNow();
        _stats.SpeechDetected = _detector.SpeechDetected;
        LogOutcome(outcome, _stats);
    }

    /// <summary>The one Information line every dictation ends with. Never includes the transcript, only its length.</summary>
    private void LogOutcome(DictationOutcome outcome, DictationStats stats) =>
        _logger.Information(
            "Dictation {Outcome:l}: recorded {RecordingMs} ms ({AudioMs} ms of audio, the first {FirstAudioMs} ms after the press), " +
            "speech detected {SpeechDetected}, peak {PeakDbfs:F1} dBFS, {Characters} characters, " +
            "release to paste {ReleaseToPasteMs} ms, target {TargetApp}",
            Describe(outcome), (long)(stats.StoppedAt - stats.StartedAt).TotalMilliseconds, stats.AudioMs, stats.FirstAudioMs,
            stats.SpeechDetected, stats.PeakDbfs, stats.Characters, stats.ReleaseToPasteMs, stats.TargetApp);

    /// <summary>The outcome as the log line names it.</summary>
    private static string Describe(DictationOutcome outcome) => outcome switch
    {
        DictationOutcome.Pasted => "pasted",
        DictationOutcome.LeftOnClipboard => "left-on-clipboard",
        DictationOutcome.SavedOnly => "saved-only",
        DictationOutcome.Silence => "silence",
        DictationOutcome.Cancelled => "cancelled",
        DictationOutcome.Failed => "failed",
        _ => outcome.ToString(),
    };

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

    /// <summary>What the pill and the Windows notification say about text that was not pasted.</summary>
    private sealed record UnpastedNotice(string Pill, string Title, string Message);

    /// <summary>What a dictation's log line reports. Holds no transcript text.</summary>
    /// <param name="startedAt">When the hotkey was pressed.</param>
    private sealed class DictationStats(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;

        /// <summary>When the recording stopped (hotkey released) or was abandoned.</summary>
        public DateTimeOffset StoppedAt { get; set; } = startedAt;

        public int AudioMs { get; set; }

        /// <summary>
        /// From the press to the first audio from the microphone: how long the owner had to wait before talking. Null
        /// when no audio came. The microphone's open is only part of it; the device takes time to deliver after that.
        /// </summary>
        public long? FirstAudioMs { get; set; }

        public bool SpeechDetected { get; set; }

        /// <summary>The loudest captured chunk's RMS level; the meter's floor until audio arrives.</summary>
        public float PeakDbfs { get; set; } = -120f;

        public int Characters { get; set; }

        /// <summary>From the release to Ctrl+V being sent (or the text left on the clipboard); null when nothing was pasted.</summary>
        public long? ReleaseToPasteMs { get; set; }

        public string TargetApp { get; set; } = "";
    }
}
