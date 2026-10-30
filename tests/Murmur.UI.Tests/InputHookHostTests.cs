using System.ComponentModel;
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
    private readonly ManualTime _time = new();
    private readonly ConcurrentQueue<HotkeySignal> _signals = new();
    private readonly ConcurrentQueue<DateTimeOffset> _stamps = new();
    private readonly InputHookHost _host;

    public InputHookHostTests() => _host = new InputHookHost(_matcher, _coordinator, _keyboard, _time, (signal, at) =>
    {
        _signals.Enqueue(signal);
        _stamps.Enqueue(at);
    });

    [Fact]
    public void OnKey_ModifierHotkeyDown_PostsHotkeyDownAndLetsKeyThrough()
    {
        _host.OnKey(Down(VkRightCtrl)).ShouldBeFalse();

        _signals.ShouldBe([HotkeySignal.HotkeyDown]);
    }

    [Fact]
    public void OnKey_StampsEachSignalWithTheTimeTheHookSawIt()
    {
        var pressedAt = _time.Now;
        _host.OnKey(Down(VkRightCtrl));
        _time.Now += TimeSpan.FromMilliseconds(150);
        _host.OnKey(Up(VkRightCtrl));

        _signals.ShouldBe([HotkeySignal.HotkeyDown, HotkeySignal.HotkeyUp]);
        _stamps.ShouldBe([pressedAt, pressedAt + TimeSpan.FromMilliseconds(150)]);
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

    /// <summary>
    /// A start that timed out is given up on and its host disposed, but its thread may still be installing the hook; a
    /// Dispose that came before the thread could take its WM_QUIT would then leave a hook running beside the retry's.
    /// The thread has to notice the Dispose when the install ends and remove the hook itself. The install is held up
    /// here (and installs nothing), so the order is the same every time.
    /// </summary>
    [Fact]
    public async Task Start_DisposedWhileTheHooksAreBeingInstalled_DoesNotKeepThem()
    {
        using var host = new SlowInstallHost(_matcher, _coordinator, _keyboard, _time);
        var start = Task.Run(host.Start, TestContext.Current.CancellationToken);
        host.Installing.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue();

        var dispose = Task.Run(host.Dispose, TestContext.Current.CancellationToken);
        SpinWait.SpinUntil(() => host.IsDisposed, TimeSpan.FromSeconds(5)).ShouldBeTrue();
        host.Installed.Set();
        await dispose;

        // Start does not report a host that was disposed meanwhile as started.
        var failure = await Record.ExceptionAsync(() => start);
        failure.ShouldNotBeNull().ShouldBeAssignableTo<OperationCanceledException>();
        host.IsRunning.ShouldBeFalse();
    }

    /// <summary>Guards the seam above: with the install let through, the host runs until it is disposed.</summary>
    [Fact]
    public void StartThenDispose_WithAnInstallThatSucceeds_RunsAndEndsTheHookThread()
    {
        using var host = new SlowInstallHost(_matcher, _coordinator, _keyboard, _time);
        host.Installed.Set();

        host.Start();
        host.IsRunning.ShouldBeTrue();
        host.Dispose();

        host.IsRunning.ShouldBeFalse();
    }

    /// <summary>The caller (and the status the web app shows) gets the installation failure itself, not a wrapper around it.</summary>
    [Fact]
    public void Start_TheInstallFails_ThrowsTheFailureItself()
    {
        using var host = new SlowInstallHost(_matcher, _coordinator, _keyboard, _time) { InstallFailure = new Win32Exception(5) };
        host.Installed.Set();

        var failure = Should.Throw<Win32Exception>(host.Start);

        failure.NativeErrorCode.ShouldBe(5);
        host.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void Dispose_NeverStarted_DoesNotThrow()
    {
        Should.NotThrow(_host.Dispose);
    }

    private static KeyInput Down(int vk) => new(vk, IsDown: true, IsInjected: false);

    private static KeyInput Up(int vk) => new(vk, IsDown: false, IsInjected: false);

    /// <summary>A host whose install waits for the test and installs nothing, so no real hook is involved.</summary>
    private sealed class SlowInstallHost(HotkeyMatcher matcher, HotkeyCaptureCoordinator coordinator, IKeyboardSender keyboard, TimeProvider time)
        : InputHookHost(matcher, coordinator, keyboard, time, static (_, _) => { })
    {
        public ManualResetEventSlim Installing { get; } = new(false);

        public ManualResetEventSlim Installed { get; } = new(false);

        /// <summary>Thrown by the install, like the Win32Exception of a hook that Windows refuses.</summary>
        public Exception? InstallFailure { get; init; }

        protected override void Install(InputHooks hooks)
        {
            Installing.Set();
            Installed.Wait(TimeSpan.FromSeconds(10));
            if (InstallFailure is not null)
                throw InstallFailure;
        }
    }

    /// <summary>A clock that only moves when the test moves it.</summary>
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
