using System.Collections.Concurrent;
using Murmur.Core.Input;
using Murmur.Core.Insertion;
using Murmur.Core.Models;
using NSubstitute;
using Shouldly;

namespace Murmur.UI.Tests;

public sealed class InputHookHostTests
{
    private const int VkRightCtrl = 0xA3;
    private const int VkRightAlt = 0xA5;
    private const int VkEscape = 0x1B;
    private const int VkA = 0x41;

    private static readonly HotkeyDefinition RightAlt = new(VkRightAlt, HotkeyModifiers.None, "Right Alt");

    private readonly HotkeyMatcher _matcher = new(HotkeyDefinition.Default);
    private readonly HotkeyCaptureCoordinator _coordinator = new(TimeProvider.System);
    private readonly IKeyboardSender _keyboard = Substitute.For<IKeyboardSender>();
    private readonly ConcurrentQueue<HotkeySignal> _signals = new();
    private readonly InputHookHost _host;

    public InputHookHostTests() => _host = new InputHookHost(_matcher, _coordinator, _keyboard, _signals.Enqueue);

    [Fact]
    public void OnKey_ModifierHotkeyDown_PostsHotkeyDownAndLetsKeyThrough()
    {
        _host.OnKey(Down(VkRightCtrl)).ShouldBeFalse();

        _signals.ShouldBe([HotkeySignal.HotkeyDown]);
    }

    [Fact]
    public void OnKey_OrdinaryKey_PostsNothing()
    {
        _host.OnKey(Down(VkA)).ShouldBeFalse();
        _host.OnKey(Up(VkA)).ShouldBeFalse();

        _signals.ShouldBeEmpty();
    }

    [Fact]
    public void OnKey_EscapeWhileDictating_SwallowsAndPostsEscape()
    {
        _matcher.DictationActive = true;

        _host.OnKey(Down(VkEscape)).ShouldBeTrue();

        _signals.ShouldBe([HotkeySignal.Escape]);
    }

    [Fact]
    public async Task OnKey_WhileCapturingHotkey_SwallowsAndPostsNothing()
    {
        using var cts = new CancellationTokenSource();
        var capture = _coordinator.CaptureAsync(TimeSpan.FromSeconds(10), cts.Token);

        _host.OnKey(Down(VkRightCtrl)).ShouldBeTrue();
        _host.OnKey(Up(VkRightCtrl)).ShouldBeTrue();

        _signals.ShouldBeEmpty();
        (await capture).ShouldNotBeNull().VirtualKey.ShouldBe(VkRightCtrl);
    }

    [Fact]
    public void OnMouse_WhileHotkeyHeld_PostsOtherInput()
    {
        _host.OnKey(Down(VkRightCtrl));

        _host.OnMouse();

        _signals.ShouldBe([HotkeySignal.HotkeyDown, HotkeySignal.OtherInputWhileHeld]);
    }

    [Fact]
    public void OnKey_AltHotkeyDown_SendsMenuMaskKeyOffTheHookThread()
    {
        _matcher.Hotkey = RightAlt;
        var senderThread = -1;
        _keyboard.When(k => k.SendMaskKey()).Do(_ => senderThread = Environment.CurrentManagedThreadId);

        _host.OnKey(Down(VkRightAlt));

        SpinWait.SpinUntil(() => senderThread != -1, TimeSpan.FromSeconds(5)).ShouldBeTrue();
        senderThread.ShouldNotBe(Environment.CurrentManagedThreadId);
        _signals.ShouldBe([HotkeySignal.HotkeyDown]);
    }

    [Fact]
    public void OnKey_AltHotkeyUp_SendsNoMenuMaskKey()
    {
        _matcher.Hotkey = RightAlt;
        _host.OnKey(Down(VkRightAlt));
        SpinWait.SpinUntil(() => _keyboard.ReceivedCalls().Any(), TimeSpan.FromSeconds(5));
        _keyboard.ClearReceivedCalls();

        _host.OnKey(Up(VkRightAlt));
        Thread.Sleep(100);

        _keyboard.DidNotReceive().SendMaskKey();
    }

    [Theory]
    [InlineData(0xA4, HotkeyModifiers.None, true)]  // Left Alt
    [InlineData(0xA5, HotkeyModifiers.None, true)]  // Right Alt
    [InlineData(0x5B, HotkeyModifiers.None, true)]  // Left Win
    [InlineData(0x5C, HotkeyModifiers.None, true)]  // Right Win
    [InlineData(0xA3, HotkeyModifiers.None, false)] // Right Ctrl
    [InlineData(0xA0, HotkeyModifiers.None, false)] // Left Shift
    [InlineData(0x20, HotkeyModifiers.Alt, false)]  // Alt+Space: the combo's main key is swallowed anyway
    [InlineData(0xA5, HotkeyModifiers.Ctrl, false)] // Ctrl+Right Alt is a combo
    public void NeedsMenuMask_OnlyForLoneAltOrWin(int vk, HotkeyModifiers modifiers, bool expected)
    {
        InputHookHost.NeedsMenuMask(new HotkeyDefinition(vk, modifiers, "x")).ShouldBe(expected);
    }

    [Fact]
    public void StartThenDispose_RunsAndEndsTheHookThread()
    {
        _host.Start();
        _host.IsRunning.ShouldBeTrue();

        _host.Dispose();

        _host.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void Dispose_NeverStarted_DoesNotThrow()
    {
        Should.NotThrow(_host.Dispose);
    }

    private static KeyInput Down(int vk) => new(vk, IsDown: true, IsInjected: false);

    private static KeyInput Up(int vk) => new(vk, IsDown: false, IsInjected: false);
}
