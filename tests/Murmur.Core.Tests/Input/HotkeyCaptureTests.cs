using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Shouldly;

namespace Murmur.Core.Tests.Input;

public sealed class HotkeyCaptureTests
{
    private const int RightCtrl = 0xA3;
    private const int LeftCtrl = 0xA2;
    private const int LeftShift = 0xA0;
    private const int LeftWin = 0x5B;
    private const int Space = 0x20;
    private const int Escape = 0x1B;
    private const int KeyA = 0x41;
    private const int F13 = 0x7C;
    private const int LeftAlt = 0xA4;
    private const int Tab = 0x09;
    private const int F4 = 0x73;

    /// <summary>The key right of P on a Norwegian keyboard: "]" on a US layout, "å" on a Norwegian one.</summary>
    private const int OemAngstrom = 0xDD;

    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    private static KeyInput Down(int vk) => new(vk, IsDown: true, IsInjected: false);

    private static KeyInput Up(int vk) => new(vk, IsDown: false, IsInjected: false);

    private static HotkeyCaptureSession Feed(params KeyInput[] keys) => Feed(FakeKeyboardLayout.Unmapped, keys);

    private static HotkeyCaptureSession Feed(IKeyboardLayout layout, params KeyInput[] keys)
    {
        var session = new HotkeyCaptureSession(layout);
        foreach (var key in keys)
            session.OnKey(key);
        return session;
    }

    [Fact]
    public void NewSession_IsIncomplete()
    {
        var session = new HotkeyCaptureSession(FakeKeyboardLayout.Unmapped);

        session.IsComplete.ShouldBeFalse();
        session.Result.ShouldBeNull();
    }

    [Fact]
    public void RightCtrlAlone()
    {
        var session = Feed(Down(RightCtrl), Up(RightCtrl));

        session.IsComplete.ShouldBeTrue();
        session.Result.ShouldBe(new HotkeyDefinition(0xA3, HotkeyModifiers.None, "Right Ctrl"));
    }

    [Fact]
    public void RightCtrlAlone_WithAutoRepeat()
    {
        var session = Feed(Down(RightCtrl), Down(RightCtrl), Down(RightCtrl), Up(RightCtrl));

        session.Result.ShouldBe(new HotkeyDefinition(0xA3, HotkeyModifiers.None, "Right Ctrl"));
    }

    [Fact]
    public void ModifierStillHeld_NotYetComplete()
    {
        var session = Feed(Down(RightCtrl));

        session.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void LeftWinAlone()
    {
        var session = Feed(Down(LeftWin), Up(LeftWin));

        session.Result.ShouldBe(new HotkeyDefinition(0x5B, HotkeyModifiers.None, "Left Win"));
    }

    [Fact]
    public void CtrlShiftSpace()
    {
        var session = Feed(Down(LeftCtrl), Down(LeftShift), Down(Space));

        session.IsComplete.ShouldBeTrue();
        session.Result.ShouldBe(new HotkeyDefinition(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Space"));
    }

    [Fact]
    public void ModifierChordReleased_KeepsWaiting()
    {
        var session = Feed(Down(LeftCtrl), Down(LeftShift), Up(LeftShift), Up(LeftCtrl));

        session.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void ModifierChordReleased_ThenSingleModifier_Completes()
    {
        var session = Feed(Down(LeftCtrl), Down(LeftShift), Up(LeftShift), Up(LeftCtrl), Down(RightCtrl), Up(RightCtrl));

        session.Result.ShouldBe(new HotkeyDefinition(0xA3, HotkeyModifiers.None, "Right Ctrl"));
    }

    [Fact]
    public void PlainLetter_IgnoredThenValidCombo()
    {
        var session = Feed(Down(KeyA), Up(KeyA));
        session.IsComplete.ShouldBeFalse();

        session.OnKey(Down(LeftCtrl));
        session.OnKey(Down(LeftShift));
        session.OnKey(Down(Space));

        session.IsComplete.ShouldBeTrue();
        session.Result.ShouldBe(new HotkeyDefinition(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Space"));
    }

    [Fact]
    public void FunctionKeyAlone_IsValid()
    {
        var session = Feed(Down(F13));

        session.Result.ShouldBe(new HotkeyDefinition(0x7C, HotkeyModifiers.None, "F13"));
    }

    [Fact]
    public void AltTab_CancelsInsteadOfCapturing()
    {
        var session = Feed(Down(LeftAlt), Down(Tab));

        session.IsComplete.ShouldBeTrue();
        session.Result.ShouldBeNull();
    }

    [Fact]
    public void AltF4_CancelsInsteadOfCapturing()
    {
        var session = Feed(Down(LeftAlt), Down(F4));

        session.IsComplete.ShouldBeTrue();
        session.Result.ShouldBeNull();
    }

    [Fact]
    public void WinTab_CancelsInsteadOfCapturing()
    {
        var session = Feed(Down(LeftWin), Down(Tab));

        session.IsComplete.ShouldBeTrue();
        session.Result.ShouldBeNull();
    }

    [Fact]
    public void CtrlWithNordicOemKey_IsNamedAfterTheLayout()
    {
        var session = Feed(FakeKeyboardLayout.Nordic, Down(LeftCtrl), Down(OemAngstrom));

        session.Result.ShouldBe(new HotkeyDefinition(OemAngstrom, HotkeyModifiers.Ctrl, "Ctrl+Å"));
    }

    [Fact]
    public void CtrlWithOemKeyTheLayoutCannotMap_IsNamedWithTheUsName()
    {
        var session = Feed(FakeKeyboardLayout.Unmapped, Down(LeftCtrl), Down(OemAngstrom));

        session.Result.ShouldBe(new HotkeyDefinition(OemAngstrom, HotkeyModifiers.Ctrl, "Ctrl+]"));
    }

    [Fact]
    public void CtrlTab_IsCaptured()
    {
        var session = Feed(Down(LeftCtrl), Down(Tab));

        session.Result.ShouldBe(new HotkeyDefinition(0x09, HotkeyModifiers.Ctrl, "Ctrl+Tab"));
    }

    [Fact]
    public void Escape_Cancels()
    {
        var session = Feed(Down(LeftCtrl), Down(Escape));

        session.IsComplete.ShouldBeTrue();
        session.Result.ShouldBeNull();
    }

    [Fact]
    public void AfterCompletion_FurtherKeysIgnored()
    {
        var session = Feed(Down(RightCtrl), Up(RightCtrl), Down(Escape));

        session.Result.ShouldBe(HotkeyDefinition.Default);
    }

    [Fact]
    public void InjectedKeys_Ignored()
    {
        var session = Feed(new KeyInput(RightCtrl, true, true), new KeyInput(RightCtrl, false, true));

        session.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void Coordinator_NoCapture_TryHandleFalse()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Unmapped);

        coordinator.TryHandle(Down(KeyA)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_OemKeyOnNordicLayout_CapturesTheLayoutsName()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Nordic);
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        coordinator.TryHandle(Down(LeftCtrl)).ShouldBeTrue();
        coordinator.TryHandle(Down(OemAngstrom)).ShouldBeTrue();

        (await capture).ShouldBe(new HotkeyDefinition(OemAngstrom, HotkeyModifiers.Ctrl, "Ctrl+Å"));
    }

    [Fact]
    public async Task Coordinator_CompletesWithCapturedHotkey_AndSwallowsKeys()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Unmapped);
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        coordinator.TryHandle(Down(LeftCtrl)).ShouldBeTrue();
        coordinator.TryHandle(Down(LeftShift)).ShouldBeTrue();
        coordinator.TryHandle(Down(Space)).ShouldBeTrue();

        (await capture).ShouldBe(new HotkeyDefinition(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Space"));
        coordinator.TryHandle(Up(Space)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_ModifierAlone_CompletingKeyUpSwallowed()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Unmapped);
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        coordinator.TryHandle(Down(RightCtrl)).ShouldBeTrue();
        coordinator.TryHandle(Up(RightCtrl)).ShouldBeTrue();

        (await capture).ShouldBe(HotkeyDefinition.Default);
    }

    [Fact]
    public async Task Coordinator_Escape_ReturnsNull()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Unmapped);
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        coordinator.TryHandle(Down(Escape)).ShouldBeTrue();

        (await capture).ShouldBeNull();
        coordinator.TryHandle(Up(Escape)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_AltTab_ReturnsNullAndStopsCapturing()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Unmapped);
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        coordinator.TryHandle(Down(LeftAlt)).ShouldBeTrue();
        coordinator.TryHandle(Down(Tab)).ShouldBeTrue();

        (await capture).ShouldBeNull();
        coordinator.TryHandle(Down(Tab)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_TimesOut_ReturnsNull()
    {
        var time = new FakeTimeProvider();
        var coordinator = new HotkeyCaptureCoordinator(time, FakeKeyboardLayout.Unmapped);
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);
        coordinator.TryHandle(Down(KeyA)).ShouldBeTrue();

        time.Advance(TimeSpan.FromSeconds(10));

        (await capture).ShouldBeNull();
        coordinator.TryHandle(Down(KeyA)).ShouldBeFalse();
    }

    [Fact]
    public void Coordinator_BeforeTimeout_StillActive()
    {
        var time = new FakeTimeProvider();
        var coordinator = new HotkeyCaptureCoordinator(time, FakeKeyboardLayout.Unmapped);
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(9));

        capture.IsCompleted.ShouldBeFalse();
        coordinator.TryHandle(Down(KeyA)).ShouldBeTrue();
    }

    [Fact]
    public async Task Coordinator_SecondCapture_CancelsFirst()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Unmapped);
        var first = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);
        var second = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        (await first).ShouldBeNull();

        coordinator.TryHandle(Down(RightCtrl)).ShouldBeTrue();
        coordinator.TryHandle(Up(RightCtrl)).ShouldBeTrue();
        (await second).ShouldBe(HotkeyDefinition.Default);
    }

    [Fact]
    public async Task Coordinator_Cancelled_ThrowsAndStopsCapturing()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Unmapped);
        using var cts = new CancellationTokenSource();
        var capture = coordinator.CaptureAsync(CaptureTimeout, cts.Token);

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(capture);
        coordinator.TryHandle(Down(KeyA)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_InvalidTimeout_ThrowsWithoutCapturing()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Unmapped);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => coordinator.CaptureAsync(TimeSpan.FromSeconds(-5), TestContext.Current.CancellationToken));

        coordinator.TryHandle(Down(KeyA)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_Cancel_CompletesWithNullAndStopsSwallowing()
    {
        // The window lost focus mid-capture: keys meant for another app must not become the hotkey.
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Unmapped);
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);
        coordinator.TryHandle(Down(LeftCtrl)).ShouldBeTrue();

        coordinator.Cancel();

        (await capture).ShouldBeNull();
        coordinator.TryHandle(Down(KeyA)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_CancelWhenIdle_DoesNothing()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Unmapped);

        coordinator.Cancel();

        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);
        coordinator.TryHandle(Down(RightCtrl)).ShouldBeTrue();
        coordinator.TryHandle(Up(RightCtrl)).ShouldBeTrue();
        (await capture).ShouldBe(HotkeyDefinition.Default);
    }

    [Fact]
    public async Task Coordinator_InjectedInput_PassesThrough()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider(), FakeKeyboardLayout.Unmapped);
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        coordinator.TryHandle(new KeyInput(0x56, true, IsInjected: true)).ShouldBeFalse();

        capture.IsCompleted.ShouldBeFalse();
        coordinator.TryHandle(Down(Escape)).ShouldBeTrue();
        (await capture).ShouldBeNull();
    }
}
