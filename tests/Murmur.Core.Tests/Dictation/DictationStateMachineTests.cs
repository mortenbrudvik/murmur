using Murmur.Core.Dictation;
using Shouldly;

namespace Murmur.Core.Tests.Dictation;

public sealed class DictationStateMachineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Ms(int n) => T0.AddMilliseconds(n);

    private static DictationStateMachine NewMachine() => new(DictationTimings.Default);

    // Unless a test says otherwise, presses are of a combo hotkey, which starts recording at once.

    /// <summary>Press at t0 and release after 800 ms: a hold, so the machine ends up Transcribing.</summary>
    private static DictationStateMachine HoldAndRelease()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);
        sm.OnHotkeyUp(Ms(800));
        return sm;
    }

    /// <summary>Press at t0 and release after 150 ms: a tap, so the machine ends up HandsFreeRecording.</summary>
    private static DictationStateMachine Tap()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);
        sm.OnHotkeyUp(Ms(150));
        return sm;
    }

    /// <summary>A lone-modifier hotkey (Right Ctrl) pressed at t0: armed, nothing started yet.</summary>
    private static DictationStateMachine Armed()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: true).ShouldBe(DictationAction.Arm);
        sm.State.ShouldBe(DictationState.Armed);
        return sm;
    }

    [Fact]
    public void Default_Timings_MatchSpec()
    {
        DictationTimings.Default.TapThreshold.ShouldBe(TimeSpan.FromMilliseconds(300));
        DictationTimings.Default.StrayTapTimeout.ShouldBe(TimeSpan.FromSeconds(8));
        DictationTimings.Default.HandsFreeLimit.ShouldBe(TimeSpan.FromMinutes(10));
        DictationTimings.Default.ArmDelay.ShouldBe(TimeSpan.FromMilliseconds(150));

        // A release while armed is always a tap.
        DictationTimings.Default.ArmDelay.ShouldBeLessThan(DictationTimings.Default.TapThreshold);
    }

    [Fact]
    public void ComboHotkey_StartsAtOnce()
    {
        var sm = NewMachine();

        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false).ShouldBe(DictationAction.StartRecording);
        sm.State.ShouldBe(DictationState.HoldRecording);
    }

    [Fact]
    public void ModifierHotkey_Pressed_ArmsWithoutStarting()
    {
        var sm = NewMachine();

        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: true).ShouldBe(DictationAction.Arm);
        sm.State.ShouldBe(DictationState.Armed);
        sm.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void ModifierHotkey_OtherInputWithinArmDelay_GoesIdleWithoutStarting()
    {
        var sm = Armed();
        sm.OnTick(Ms(100), hotkeyPhysicallyDown: true).ShouldBe(DictationAction.None);

        sm.OnOtherInputWhileHeld().ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.Idle);

        // The shortcut's own release, and the polls after it, start nothing either.
        sm.OnHotkeyUp(Ms(200)).ShouldBe(DictationAction.None);
        sm.OnTick(Ms(300), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void ModifierHotkey_HeldPastArmDelay_StartsRecording()
    {
        var sm = Armed();

        sm.OnTick(Ms(149), hotkeyPhysicallyDown: true).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.Armed);
        sm.OnTick(Ms(150), hotkeyPhysicallyDown: true).ShouldBe(DictationAction.StartRecording);
        sm.State.ShouldBe(DictationState.HoldRecording);

        sm.OnHotkeyUp(Ms(800)).ShouldBe(DictationAction.StopAndPaste);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void ModifierHotkey_ReleasedWithinArmDelay_StartsHandsFree()
    {
        var sm = Armed();

        sm.OnHotkeyUp(Ms(100)).ShouldBe(DictationAction.StartRecording);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);

        // The hands-free timers run from the press.
        sm.OnTick(Ms(7999), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.None);
        sm.OnTick(Ms(8000), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.Cancel);
    }

    [Fact]
    public void ModifierHotkey_ReleasedAfterArmDelayButBeforeTapThreshold_IsATap()
    {
        var sm = Armed();
        sm.OnTick(Ms(150), hotkeyPhysicallyDown: true).ShouldBe(DictationAction.StartRecording);

        sm.OnHotkeyUp(Ms(250)).ShouldBe(DictationAction.None); // timed from the press, not from the start
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
    }

    [Fact]
    public void ModifierHotkey_ReleaseMissedWhileArmed_GoesIdleWithoutStarting()
    {
        var sm = Armed();

        sm.OnTick(Ms(150), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void ModifierHotkey_EscapeWhileArmed_GoesIdleWithoutCancelling()
    {
        var sm = Armed();

        sm.OnEscape().ShouldBe(DictationAction.None); // nothing was started, so there is nothing to cancel
        sm.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void ModifierHotkey_SpeechWhileArmed_IsIgnored()
    {
        var sm = Armed();

        sm.OnSpeechDetected();

        sm.SpeechSeen.ShouldBeFalse();
    }

    [Fact]
    public void HandsFree_SecondTap_StopsWithoutArming()
    {
        var sm = Armed();
        sm.OnHotkeyUp(Ms(100)).ShouldBe(DictationAction.StartRecording);

        sm.OnHotkeyDown(Ms(3000), engineReady: true, modifierOnly: true).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
        sm.OnTick(Ms(3200), hotkeyPhysicallyDown: true).ShouldBe(DictationAction.None); // past the arm delay: no start
        sm.OnHotkeyUp(Ms(3300)).ShouldBe(DictationAction.StopAndPaste);
        sm.State.ShouldBe(DictationState.Transcribing);
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

        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false).ShouldBe(DictationAction.StartRecording);
        sm.State.ShouldBe(DictationState.HoldRecording);
        sm.OnHotkeyUp(Ms(800)).ShouldBe(DictationAction.StopAndPaste);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void Hold_ReleasedExactlyAtThreshold_IsAHold()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);

        sm.OnHotkeyUp(Ms(300)).ShouldBe(DictationAction.StopAndPaste);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void Tap_StartsHandsFree()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);

        sm.OnHotkeyUp(Ms(150)).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
        sm.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Tap_ReleasedJustBeforeThreshold_IsATap()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);

        sm.OnHotkeyUp(Ms(299)).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
    }

    [Fact]
    public void HandsFree_SecondTap_StopsAndPastes()
    {
        var sm = Tap();

        sm.OnHotkeyDown(Ms(3000), engineReady: true, modifierOnly: false).ShouldBe(DictationAction.None);
        sm.OnHotkeyUp(Ms(3100)).ShouldBe(DictationAction.StopAndPaste);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void HandsFree_SecondTapUsedInCombo_KeepsRecording()
    {
        var sm = Tap();

        sm.OnHotkeyDown(Ms(3000), engineReady: true, modifierOnly: false);
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
        sm.OnHotkeyDown(Ms(3000), engineReady: true, modifierOnly: false);

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
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);

        sm.OnOtherInputWhileHeld().ShouldBe(DictationAction.Cancel);
        sm.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void Hold_MissedRelease_TickRecovers()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);

        sm.OnTick(Ms(900), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.StopAndPaste);
        sm.State.ShouldBe(DictationState.Transcribing);
    }

    [Fact]
    public void Hold_MissedRelease_TickWithinTapThreshold_StartsHandsFree()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);

        sm.OnTick(Ms(100), hotkeyPhysicallyDown: false).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HandsFreeRecording);
    }

    [Fact]
    public void Hold_TickWhileStillHeld_DoesNothing()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);

        sm.OnTick(Ms(900), hotkeyPhysicallyDown: true).ShouldBe(DictationAction.None);
        sm.State.ShouldBe(DictationState.HoldRecording);
    }

    [Fact]
    public void Escape_WhileRecording_Cancels()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EngineNotReady_NotifiesAndStaysIdle(bool modifierOnly)
    {
        var sm = NewMachine();

        sm.OnHotkeyDown(T0, engineReady: false, modifierOnly).ShouldBe(DictationAction.NotifyNotReady);
        sm.State.ShouldBe(DictationState.Idle);
        sm.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void HotkeyDuringTranscription_Ignored()
    {
        var sm = HoldAndRelease();

        sm.OnHotkeyDown(Ms(900), engineReady: true, modifierOnly: false).ShouldBe(DictationAction.None);
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
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);

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
        sm.OnHotkeyDown(Ms(20_000), engineReady: true, modifierOnly: false);
        sm.SpeechSeen.ShouldBeFalse();
    }

    [Fact]
    public void Reset_ReturnsToIdle_AndClearsFlags()
    {
        var sm = NewMachine();
        sm.OnHotkeyDown(T0, engineReady: true, modifierOnly: false);
        sm.OnSpeechDetected();

        sm.Reset();

        sm.State.ShouldBe(DictationState.Idle);
        sm.IsActive.ShouldBeFalse();
        sm.SpeechSeen.ShouldBeFalse();
    }
}
