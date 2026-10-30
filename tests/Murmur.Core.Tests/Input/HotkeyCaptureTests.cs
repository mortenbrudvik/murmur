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

    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    private static KeyInput Down(int vk) => new(vk, IsDown: true, IsInjected: false);

    private static KeyInput Up(int vk) => new(vk, IsDown: false, IsInjected: false);

    private static HotkeyCaptureSession Feed(params KeyInput[] keys)
    {
        var session = new HotkeyCaptureSession();
        foreach (var key in keys)
            session.OnKey(key);
        return session;
    }

    [Fact]
    public void NewSession_IsIncomplete()
    {
        var session = new HotkeyCaptureSession();

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
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider());

        coordinator.TryHandle(Down(KeyA)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_CompletesWithCapturedHotkey_AndSwallowsKeys()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider());
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
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider());
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        coordinator.TryHandle(Down(RightCtrl)).ShouldBeTrue();
        coordinator.TryHandle(Up(RightCtrl)).ShouldBeTrue();

        (await capture).ShouldBe(HotkeyDefinition.Default);
    }

    [Fact]
    public async Task Coordinator_Escape_ReturnsNull()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider());
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        coordinator.TryHandle(Down(Escape)).ShouldBeTrue();

        (await capture).ShouldBeNull();
        coordinator.TryHandle(Up(Escape)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_TimesOut_ReturnsNull()
    {
        var time = new FakeTimeProvider();
        var coordinator = new HotkeyCaptureCoordinator(time);
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
        var coordinator = new HotkeyCaptureCoordinator(time);
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(9));

        capture.IsCompleted.ShouldBeFalse();
        coordinator.TryHandle(Down(KeyA)).ShouldBeTrue();
    }

    [Fact]
    public async Task Coordinator_SecondCapture_CancelsFirst()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider());
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
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider());
        using var cts = new CancellationTokenSource();
        var capture = coordinator.CaptureAsync(CaptureTimeout, cts.Token);

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(capture);
        coordinator.TryHandle(Down(KeyA)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_InvalidTimeout_ThrowsWithoutCapturing()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider());

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => coordinator.CaptureAsync(TimeSpan.FromSeconds(-5), TestContext.Current.CancellationToken));

        coordinator.TryHandle(Down(KeyA)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_Cancel_CompletesWithNullAndStopsSwallowing()
    {
        // The window lost focus mid-capture: keys meant for another app must not become the hotkey.
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider());
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);
        coordinator.TryHandle(Down(LeftCtrl)).ShouldBeTrue();

        coordinator.Cancel();

        (await capture).ShouldBeNull();
        coordinator.TryHandle(Down(KeyA)).ShouldBeFalse();
    }

    [Fact]
    public async Task Coordinator_CancelWhenIdle_DoesNothing()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider());

        coordinator.Cancel();

        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);
        coordinator.TryHandle(Down(RightCtrl)).ShouldBeTrue();
        coordinator.TryHandle(Up(RightCtrl)).ShouldBeTrue();
        (await capture).ShouldBe(HotkeyDefinition.Default);
    }

    [Fact]
    public async Task Coordinator_InjectedInput_PassesThrough()
    {
        var coordinator = new HotkeyCaptureCoordinator(new FakeTimeProvider());
        var capture = coordinator.CaptureAsync(CaptureTimeout, TestContext.Current.CancellationToken);

        coordinator.TryHandle(new KeyInput(0x56, true, IsInjected: true)).ShouldBeFalse();

        capture.IsCompleted.ShouldBeFalse();
        coordinator.TryHandle(Down(Escape)).ShouldBeTrue();
        (await capture).ShouldBeNull();
    }
}
