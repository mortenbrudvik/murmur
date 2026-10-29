namespace Murmur.Core.Dictation;

public enum DictationState
{
    Idle,
    HoldRecording,
    HandsFreeRecording,
    Transcribing,
}

public enum DictationAction
{
    None,
    StartRecording,
    StopAndPaste,
    StopAndSaveOnly,
    Cancel,
    NotifyNotReady,
}

/// <param name="TapThreshold">A press released sooner than this is a tap (starts hands-free); otherwise it was a hold.</param>
/// <param name="StrayTapTimeout">A hands-free session in which no speech was heard is cancelled after this long.</param>
/// <param name="HandsFreeLimit">A hands-free session is stopped (audio saved, not pasted) after this long.</param>
public sealed record DictationTimings(TimeSpan TapThreshold, TimeSpan StrayTapTimeout, TimeSpan HandsFreeLimit)
{
    public static DictationTimings Default { get; } =
        new(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(8), TimeSpan.FromMinutes(10));
}

/// <summary>
/// Pure, deterministic state machine for the dictation flow: hold the hotkey to talk, tap it for hands-free
/// recording, tap again to stop, Esc to cancel. It performs no I/O and never reads the clock; callers pass the
/// current time and execute the returned <see cref="DictationAction"/>. Not thread-safe: drive it from one thread.
/// </summary>
public sealed class DictationStateMachine(DictationTimings timings)
{
    // When the hotkey went down. Also the start of a hands-free session, so the stray-tap and
    // hands-free timers run from the press, not from the tap's release.
    private DateTimeOffset _pressedAt;

    // In hands-free mode: the hotkey is currently down for the (potential) stopping tap.
    private bool _secondTapHeld;

    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>True while a dictation session is in progress (anything but <see cref="DictationState.Idle"/>).</summary>
    public bool IsActive => State != DictationState.Idle;

    /// <summary>True once speech was reported during the current (or just finished) session.</summary>
    public bool SpeechSeen { get; private set; }

    public DictationAction OnHotkeyDown(DateTimeOffset now, bool engineReady)
    {
        switch (State)
        {
            case DictationState.Idle:
                if (!engineReady)
                    return DictationAction.NotifyNotReady;

                _pressedAt = now;
                SpeechSeen = false;
                return Enter(DictationState.HoldRecording, DictationAction.StartRecording);

            case DictationState.HandsFreeRecording:
                _secondTapHeld = true;
                return DictationAction.None;

            default:
                return DictationAction.None;
        }
    }

    public DictationAction OnHotkeyUp(DateTimeOffset now)
    {
        switch (State)
        {
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

    public DictationAction OnEscape() =>
        IsActive ? Enter(DictationState.Idle, DictationAction.Cancel) : DictationAction.None;

    public void OnSpeechDetected()
    {
        if (State is DictationState.HoldRecording or DictationState.HandsFreeRecording)
            SpeechSeen = true;
    }

    /// <summary>
    /// Periodic poll. <paramref name="hotkeyPhysicallyDown"/> is the hotkey's real state, which lets the machine
    /// recover from a key-up event that was never delivered.
    /// </summary>
    public DictationAction OnTick(DateTimeOffset now, bool hotkeyPhysicallyDown)
    {
        switch (State)
        {
            case DictationState.HoldRecording:
                // A missed release is treated exactly like the release arriving now.
                return hotkeyPhysicallyDown ? DictationAction.None : ReleaseHold(now);

            case DictationState.HandsFreeRecording:
                if (_secondTapHeld && !hotkeyPhysicallyDown)
                    return Enter(DictationState.Transcribing, DictationAction.StopAndPaste);

                var elapsed = now - _pressedAt;

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

    private DictationAction ReleaseHold(DateTimeOffset now) =>
        now - _pressedAt < timings.TapThreshold
            ? Enter(DictationState.HandsFreeRecording, DictationAction.None)
            : Enter(DictationState.Transcribing, DictationAction.StopAndPaste);

    private DictationAction Enter(DictationState next, DictationAction action)
    {
        State = next;
        _secondTapHeld = false;
        return action;
    }
}
