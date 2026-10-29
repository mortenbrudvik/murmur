using Murmur.Core.Dictation;
using Shouldly;

namespace Murmur.Core.Tests.Dictation;

public sealed class DictationStateMachineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Ms(int n) => T0.AddMilliseconds(n);

    private static DictationStateMachine NewMachine() => new(DictationTimings.Default);

    /// <summary>Press at t0 and release after 800 ms: a hold, so the machine ends up Transcribing.</summary>
    private static DictationStateMachine HoldAndRelease()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);
        sm.OnHotkeyUp(Ms(800));
        return sm;
    }

    /// <summary>Press at t0 and release after 150 ms: a tap, so the machine ends up HandsFreeRecording.</summary>
    private static DictationStateMachine Tap()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);
        sm.OnHotkeyUp(Ms(150));
        return sm;
    }

    [Fact]
    public void Default_Timings_MatchSpec()
    {
        DictationTimings.Default.TapThreshold.ShouldBe(TimeSpan.FromMilliseconds(300));
        DictationTimings.Default.StrayTapTimeout.ShouldBe(TimeSpan.FromSeconds(8));
        DictationTimings.Default.HandsFreeLimit.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void NewMachine_IsIdle()
    {
        var sm = NewMachine();

        sm.State.ShouldBe(DictationState.Idle);
        sm.IsActive.ShouldBeFalse();
        sm.SpeechSeen.ShouldBeFalse();
    }

    [Fact]
    public void Hold_ReleaseAfterThreshold_StopsAndPastes()
    {
        var sm = NewMachine();

        sm.OnHotkeyDown(T0, engineReady: true).ShouldBe(DictationAction.StartRecording);
        sm.State.ShouldBe(DictationState.HoldRecording);
        sm.OnHotkeyUp(Ms(800)).ShouldBe(DictationAction.StopAndPaste);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void Hold_ReleasedExactlyAtThreshold_IsAHold()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);

        sm.OnHotkeyUp(Ms(300)).ShouldBe(DictationAction.StopAndPaste);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void Tap_StartsHandsFree()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);

        sm.OnHotkeyUp(Ms(150)).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
        sm.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Tap_ReleasedJustBeforeThreshold_IsATap()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);

        sm.OnHotkeyUp(Ms(299)).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
    }

    [Fact]
    public void HandsFree_SecondTap_StopsAndPastes()
    {
        var sm = Tap();

        sm.OnHotkeyDown(Ms(3000), engineReady: true).ShouldBe(DictationAction.None);
        sm.OnHotkeyUp(Ms(3100)).ShouldBe(DictationAction.StopAndPaste);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void HandsFree_SecondTapUsedInCombo_KeepsRecording()
    {
        var sm = Tap();

        sm.OnHotkeyDown(Ms(3000), engineReady: true);
        sm.OnOtherInputWhileHeld().ShouldBe(DictationAction.None);

        sm.OnHotkeyUp(Ms(3100)).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
    }

    [Fact]
    public void HandsFree_ReleaseWithoutSecondTapDown_Ignored()
    {
        var sm = Tap();

        sm.OnHotkeyUp(Ms(3000)).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
    }

    [Fact]
    public void HandsFree_MissedSecondRelease_TickRecovers()
    {
        var sm = Tap();
        sm.OnHotkeyDown(Ms(3000), engineReady: true);

        sm.OnTick(Ms(3500), hotkeyPhysicallyDown: true).ShouldBe(DictationAction.None);
        sm.OnTick(Ms(3600), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.StopAndPaste);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void HandsFree_TickWithKeyUpButNoSecondTap_DoesNothing()
    {
        var sm = Tap();
        sm.OnSpeechDetected();

        sm.OnTick(Ms(3000), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
    }

    [Fact]
    public void HandsFree_NoSpeechFor8s_Cancels()
    {
        var sm = Tap();

        sm.OnTick(Ms(7900), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.None);
        sm.OnTick(Ms(8000), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.Cancel);
        sm.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void HandsFree_SpeechThenSilence_DoesNotAutoCancel()
    {
        var sm = Tap();
        sm.OnSpeechDetected();

        sm.OnTick(Ms(60_000), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
    }

    [Fact]
    public void HandsFree_At10Minutes_StopsAndSavesOnly()
    {
        var sm = Tap();
        sm.OnSpeechDetected();

        sm.OnTick(T0.AddMinutes(10).AddMilliseconds(-1), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.None);
        sm.OnTick(T0.AddMinutes(10), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.StopAndSaveOnly);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void HandsFree_Timers_AreMeasuredFromPressNotRelease()
    {
        // Press at t0, tap released at 150 ms: the 8 s guard fires 8 s after the press, not after the release.
        var sm = Tap();

        sm.OnTick(Ms(8000), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.Cancel);
    }

    [Fact]
    public void Hold_OtherInputWhileHeld_Cancels()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);

        sm.OnOtherInputWhileHeld().ShouldBe(DictationAction.Cancel);
        sm.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void Hold_MissedRelease_TickRecovers()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);

        sm.OnTick(Ms(900), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.StopAndPaste);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void Hold_MissedRelease_TickWithinTapThreshold_StartsHandsFree()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);

        sm.OnTick(Ms(100), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
    }

    [Fact]
    public void Hold_TickWhileStillHeld_DoesNothing()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);

        sm.OnTick(Ms(900), hotkeyPhysicallyDown: true).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HoldRecording);
    }

    [Fact]
    public void Escape_WhileRecording_Cancels()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);

        sm.OnEscape().ShouldBe(DictationAction.Cancel);
        sm.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void Escape_WhileHandsFree_Cancels()
    {
        var sm = Tap();

        sm.OnEscape().ShouldBe(DictationAction.Cancel);
        sm.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void Escape_WhileTranscribing_Cancels()
    {
        var sm = HoldAndRelease();

        sm.OnEscape().ShouldBe(DictationAction.Cancel);
        sm.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void Escape_WhenIdle_None()
    {
        var sm = NewMachine();

        sm.OnEscape().ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void EngineNotReady_NotifiesAndStaysIdle()
    {
        var sm = NewMachine();

        sm.OnHotkeyDown(T0, engineReady: false).ShouldBe(DictationAction.NotifyNotReady);
        sm.State.ShouldBe(DictationState.Idle);
        sm.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void HotkeyDuringTranscription_Ignored()
    {
        var sm = HoldAndRelease();

        sm.OnHotkeyDown(Ms(900), engineReady: true).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void Transcribing_IgnoresUpTickAndOtherInput()
    {
        var sm = HoldAndRelease();

        sm.OnHotkeyUp(Ms(900)).ShouldBe(DictationAction.None);
        sm.OnTick(Ms(1000), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.None);
        sm.OnTick(T0.AddMinutes(30), hotkeyPhysicallyDown: true).ShouldBe(DictationAction.None);
        sm.OnOtherInputWhileHeld().ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void Idle_IgnoresUpTickAndOtherInput()
    {
        var sm = NewMachine();

        sm.OnHotkeyUp(T0).ShouldBe(DictationAction.None);
        sm.OnTick(Ms(100), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.None);
        sm.OnOtherInputWhileHeld().ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void TranscriptionFinished_ReturnsToIdle()
    {
        var sm = HoldAndRelease();

        sm.OnTranscriptionFinished();

        sm.State.ShouldBe(DictationState.Idle);
        sm.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void TranscriptionFinished_WhileRecording_IsIgnored()
    {
        // A stale completion (e.g. from a transcription cancelled with Esc) must not end a newer recording.
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);

        sm.OnTranscriptionFinished();

        sm.State.ShouldBe(DictationState.HoldRecording);
    }

    [Fact]
    public void SpeechDetected_SetsSpeechSeen_AndNextPressClearsIt()
    {
        var sm = Tap();
        sm.SpeechSeen.ShouldBeFalse();

        sm.OnSpeechDetected();
        sm.SpeechSeen.ShouldBeTrue();

        sm.OnEscape();
        sm.OnHotkeyDown(Ms(20_000), engineReady: true);
        sm.SpeechSeen.ShouldBeFalse();
    }

    [Fact]
    public void Reset_ReturnsToIdle_AndClearsFlags()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true);
        sm.OnSpeechDetected();

        sm.Reset();

        sm.State.ShouldBe(DictationState.Idle);
        sm.IsActive.ShouldBeFalse();
        sm.SpeechSeen.ShouldBeFalse();
    }
}
