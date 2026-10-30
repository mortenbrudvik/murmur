namespace Murmur.Core.Dictation;

public enum DictationState
{
    Idle,

    /// <summary>
    /// A lone-modifier hotkey (Right Ctrl) is down, but not for long enough yet to tell a dictation from another app's
    /// shortcut (Right Ctrl+C). Nothing has started: no microphone, no pill, no cue.
    /// </summary>
    Armed,

    HoldRecording,
    HandsFreeRecording,
    Transcribing,
}

public enum DictationAction
{
    None,

    /// <summary>Start nothing yet, but poll (<see cref="DictationStateMachine.OnTick"/>) until the press is decided.</summary>
    Arm,

    StartRecording,
    StopAndPaste,
    StopAndSaveOnly,
    Cancel,

    /// <summary>The press is a dictation but the engine cannot transcribe: say why. Nothing starts.</summary>
    NotifyNotReady,
}

/// <param name="TapThreshold">A press released sooner than this is a tap (starts hands-free); otherwise it was a hold.</param>
/// <param name="StrayTapTimeout">A hands-free session in which no speech was heard is cancelled after this long.</param>
/// <param name="HandsFreeLimit">A hands-free session is stopped (audio saved, not pasted) after this long.</param>
/// <param name="ArmDelay">
/// How long a lone-modifier hotkey is held before recording starts, so that using it in another app's shortcut never
/// opens the microphone. Must be shorter than <paramref name="TapThreshold"/>: a release while armed is a tap.
/// </param>
public sealed record DictationTimings(TimeSpan TapThreshold, TimeSpan StrayTapTimeout, TimeSpan HandsFreeLimit, TimeSpan ArmDelay)
{
    public static DictationTimings Default { get; } =
        new(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(8), TimeSpan.FromMinutes(10), TimeSpan.FromMilliseconds(150));
}

/// <summary>
/// Pure, deterministic state machine for the dictation flow: hold the hotkey to talk, tap it for hands-free
/// recording, tap again to stop, Esc to cancel. A lone-modifier hotkey arms first and starts recording only once it
/// has been held for <see cref="DictationTimings.ArmDelay"/> or released as a tap; another key in between means it was
/// part of a shortcut, and nothing starts (whether the engine is ready is only asked once the press is a dictation). It
/// performs no I/O and never reads the clock; callers pass the current time and execute the returned
/// <see cref="DictationAction"/>. Not thread-safe: drive it from one thread.
/// </summary>
public sealed class DictationStateMachine(DictationTimings timings)
{
    // In hands-free mode: the hotkey is currently down for the (potential) stopping tap.
    private bool _secondTapHeld;

    /// <summary>The timings the machine decides by; the caller schedules its polls from them.</summary>
    public DictationTimings Timings => timings;

    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>
    /// When the hotkey went down for the current (or last) session. Also the start of a hands-free session, so the
    /// stray-tap and hands-free timers run from the press, not from the tap's release; and tap or hold is measured from
    /// it, also when recording started only after the arm delay.
    /// </summary>
    public DateTimeOffset PressedAt { get; private set; }

    /// <summary>True while a dictation session is in progress or armed (anything but <see cref="DictationState.Idle"/>).</summary>
    public bool IsActive => State != DictationState.Idle;

    /// <summary>True once speech was reported during the current (or just finished) session.</summary>
    public bool SpeechSeen { get; private set; }

    /// <param name="engineReady">
    /// Whether the engine can transcribe. Only a combo or F-key press is decided on it here; an armed press is decided
    /// when the arm resolves.
    /// </param>
    /// <param name="modifierOnly">
    /// The hotkey is a lone modifier, which other apps' shortcuts also use, so it arms first. A combo or F-key hotkey is
    /// kept from other apps, so it starts at once.
    /// </param>
    public DictationAction OnHotkeyDown(DateTimeOffset now, bool engineReady, bool modifierOnly)
    {
        switch (State)
        {
            case DictationState.Idle:
                // A lone modifier arms whatever the engine's state: if the press turns out to be a shortcut, nothing at
                // all may show, not even the not-ready notice.
                if (!modifierOnly && !engineReady)
                    return DictationAction.NotifyNotReady;

                PressedAt = now;
                SpeechSeen = false;
                return modifierOnly
                    ? Enter(DictationState.Armed, DictationAction.Arm)
                    : Enter(DictationState.HoldRecording, DictationAction.StartRecording);

            case DictationState.HandsFreeRecording:
                // The second tap stops this session; it never arms a new one.
                _secondTapHeld = true;
                return DictationAction.None;

            default:
                return DictationAction.None;
        }
    }

    /// <param name="engineReady">Whether the engine can transcribe; decides a tap that ends an armed press.</param>
    public DictationAction OnHotkeyUp(DateTimeOffset now, bool engineReady)
    {
        switch (State)
        {
            case DictationState.Armed:
                // Released within the arm delay, which is shorter than the tap threshold: a tap.
                return StartArmed(DictationState.HandsFreeRecording, engineReady);

            case DictationState.HoldRecording:
                return ReleaseHold(now);

            case DictationState.HandsFreeRecording when _secondTapHeld:
                return Enter(DictationState.Transcribing, DictationAction.StopAndPaste);

            default:
                return DictationAction.None;
        }
    }

    /// <summary>Another key or mouse button was used while the hotkey was held (i.e. the hotkey is part of a combo).</summary>
    public DictationAction OnOtherInputWhileHeld()
    {
        switch (State)
        {
            case DictationState.Armed:
                // A shortcut such as Right Ctrl+C: nothing was started, so there is nothing to undo.
                return Enter(DictationState.Idle, DictationAction.None);

            case DictationState.HoldRecording:
                return Enter(DictationState.Idle, DictationAction.Cancel);

            case DictationState.HandsFreeRecording:
                // The second press is a combo, not a stop tap.
                _secondTapHeld = false;
                return DictationAction.None;

            default:
                return DictationAction.None;
        }
    }

    public DictationAction OnEscape() => State switch
    {
        DictationState.Idle => DictationAction.None,
        DictationState.Armed => Enter(DictationState.Idle, DictationAction.None), // nothing was started
        _ => Enter(DictationState.Idle, DictationAction.Cancel),
    };

    public void OnSpeechDetected()
    {
        if (State is DictationState.HoldRecording or DictationState.HandsFreeRecording)
            SpeechSeen = true;
    }

    /// <summary>
    /// Periodic poll. <paramref name="hotkeyPhysicallyDown"/> is the hotkey's real state, which lets the machine
    /// recover from a key-up event that was never delivered.
    /// </summary>
    /// <param name="engineReady">Whether the engine can transcribe; decides an armed press held past the arm delay.</param>
    public DictationAction OnTick(DateTimeOffset now, bool hotkeyPhysicallyDown, bool engineReady)
    {
        switch (State)
        {
            case DictationState.Armed:
                // Up without a key-up event (missed, or still on its way): a tap cannot be told from the end of a
                // shortcut, so nothing starts.
                if (!hotkeyPhysicallyDown)
                    return Enter(DictationState.Idle, DictationAction.None);

                return now - PressedAt >= timings.ArmDelay
                    ? StartArmed(DictationState.HoldRecording, engineReady)
                    : DictationAction.None;

            case DictationState.HoldRecording:
                // A missed release is treated exactly like the release arriving now.
                return hotkeyPhysicallyDown ? DictationAction.None : ReleaseHold(now);

            case DictationState.HandsFreeRecording:
                if (_secondTapHeld && !hotkeyPhysicallyDown)
                    return Enter(DictationState.Transcribing, DictationAction.StopAndPaste);

                var elapsed = now - PressedAt;

                if (!SpeechSeen && elapsed >= timings.StrayTapTimeout)
                    return Enter(DictationState.Idle, DictationAction.Cancel);

                if (elapsed >= timings.HandsFreeLimit)
                    return Enter(DictationState.Transcribing, DictationAction.StopAndSaveOnly);

                return DictationAction.None;

            default:
                return DictationAction.None;
        }
    }

    /// <summary>
    /// Transcription (and any pasting) is done. Only meaningful while <see cref="DictationState.Transcribing"/>:
    /// a late completion for a session that was cancelled with Esc must not end a newer recording.
    /// </summary>
    public void OnTranscriptionFinished()
    {
        if (State == DictationState.Transcribing)
            Enter(DictationState.Idle, DictationAction.None);
    }

    public void Reset()
    {
        Enter(DictationState.Idle, DictationAction.None);
        SpeechSeen = false;
    }

    /// <summary>
    /// An armed press turned out to be a dictation: it starts in <paramref name="recording"/>, or, when the engine cannot
    /// transcribe, ends with the not-ready notice.
    /// </summary>
    private DictationAction StartArmed(DictationState recording, bool engineReady) =>
        engineReady
            ? Enter(recording, DictationAction.StartRecording)
            : Enter(DictationState.Idle, DictationAction.NotifyNotReady);

    private DictationAction ReleaseHold(DateTimeOffset now) =>
        now - PressedAt < timings.TapThreshold
            ? Enter(DictationState.HandsFreeRecording, DictationAction.None)
            : Enter(DictationState.Transcribing, DictationAction.StopAndPaste);

    private DictationAction Enter(DictationState next, DictationAction action)
    {
        State = next;
        _secondTapHeld = false;
        return action;
    }
}
