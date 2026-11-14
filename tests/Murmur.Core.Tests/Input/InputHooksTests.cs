using System.Collections.Concurrent;
using Murmur.Core.Input;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Shouldly;

namespace Murmur.Core.Tests.Input;

public sealed class InputHooksTests
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_CHAR = 0x0102;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const uint LLKHF_EXTENDED = 0x01;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLKHF_ALTDOWN = 0x20;
    private const uint LLMHF_INJECTED = 0x01;

    [Fact]
    public void IsAltGrFakeCtrl_SyntheticLeftCtrlFromAltGr_True()
    {
        InputHooks.IsAltGrFakeCtrl(0xA2, 0x21D).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0xA2u, 0x1Du)]  // real Left Ctrl
    [InlineData(0xA3u, 0x1Du)]  // Right Ctrl (extended flag is separate; scan code stays 0x1D)
    [InlineData(0xA5u, 0x38u)]  // Right Alt itself
    [InlineData(0xA3u, 0x21Du)] // only Left Ctrl is ever faked
    public void IsAltGrFakeCtrl_OtherKeys_False(uint vk, uint scanCode)
    {
        InputHooks.IsAltGrFakeCtrl(vk, scanCode).ShouldBeFalse();
    }

    [Theory]
    [InlineData(WM_KEYDOWN, 0x41u, 0x1Eu, 0u, true, false)]                      // A down
    [InlineData(WM_KEYUP, 0x41u, 0x1Eu, 0u, false, false)]                       // A up
    [InlineData(WM_SYSKEYDOWN, 0xA5u, 0x38u, LLKHF_EXTENDED | LLKHF_ALTDOWN, true, false)] // Right Alt down
    [InlineData(WM_SYSKEYUP, 0xA5u, 0x38u, LLKHF_EXTENDED, false, false)]       // Right Alt up
    [InlineData(WM_SYSKEYDOWN, 0x5Au, 0x2Cu, LLKHF_ALTDOWN, true, false)]        // Z down while Alt is held
    [InlineData(WM_KEYDOWN, 0x56u, 0x2Fu, LLKHF_INJECTED, true, true)]           // V down from SendInput
    [InlineData(WM_KEYUP, 0x56u, 0x2Fu, LLKHF_INJECTED, false, true)]            // V up from SendInput
    [InlineData(WM_SYSKEYUP, 0xA4u, 0x38u, LLKHF_INJECTED, false, true)]         // injected Left Alt up
    [InlineData(WM_KEYDOWN, 0xA2u, 0x1Du, 0u, true, false)]                      // real Left Ctrl down
    public void TryTranslateKey_KeyDownOrUp_TranslatesKeyDirectionAndInjection(
        int message, uint vk, uint scanCode, uint flags, bool expectedDown, bool expectedInjected)
    {
        InputHooks.TryTranslateKey(message, vk, scanCode, flags, out var input).ShouldBeTrue();

        input.ShouldBe(new KeyInput((int)vk, expectedDown, expectedInjected));
    }

    [Theory]
    [InlineData(WM_KEYDOWN, 0xA2u, 0x21Du)]    // synthetic Left Ctrl before AltGr, down
    [InlineData(WM_KEYUP, 0xA2u, 0x21Du)]      // and up
    [InlineData(WM_SYSKEYDOWN, 0xA2u, 0x21Du)] // and as a system key
    [InlineData(WM_CHAR, 0x41u, 0x1Eu)]        // not a key down or up
    [InlineData(0, 0x41u, 0x1Eu)]
    public void TryTranslateKey_AltGrFakeCtrlOrOtherMessage_False(int message, uint vk, uint scanCode)
    {
        InputHooks.TryTranslateKey(message, vk, scanCode, 0, out var input).ShouldBeFalse();

        input.ShouldBe(default);
    }

    [Theory]
    [InlineData(WM_LBUTTONDOWN)]
    [InlineData(WM_RBUTTONDOWN)]
    [InlineData(WM_MBUTTONDOWN)]
    [InlineData(WM_XBUTTONDOWN)]
    [InlineData(WM_MOUSEWHEEL)]
    [InlineData(WM_MOUSEHWHEEL)]
    public void IsReportedMouseEvent_ButtonDownOrWheel_True(int message)
    {
        InputHooks.IsReportedMouseEvent(message, 0).ShouldBeTrue();
    }

    [Theory]
    [InlineData(WM_LBUTTONDOWN, LLMHF_INJECTED)] // injected button down
    [InlineData(WM_MOUSEWHEEL, LLMHF_INJECTED)]  // injected wheel
    [InlineData(WM_MOUSEMOVE, 0u)]               // movement
    [InlineData(WM_LBUTTONUP, 0u)]               // button up
    public void IsReportedMouseEvent_InjectedOrOtherMessage_False(int message, uint flags)
    {
        InputHooks.IsReportedMouseEvent(message, flags).ShouldBeFalse();
    }

    [Fact]
    public void OnKeyboardEvent_HandlerReturnsTrue_SwallowsTheEvent()
    {
        using var hooks = new InputHooks();
        var received = new List<KeyInput>();
        hooks.KeyHandler = input =>
        {
            received.Add(input);
            return true;
        };

        hooks.OnKeyboardEvent(WM_SYSKEYDOWN, 0xA5, 0x38, LLKHF_EXTENDED).ShouldBeTrue();

        received.ShouldBe([new KeyInput(0xA5, IsDown: true, IsInjected: false)]);
    }

    [Fact]
    public void OnKeyboardEvent_HandlerReturnsFalse_PassesTheEventOn()
    {
        using var hooks = new InputHooks();
        hooks.KeyHandler = _ => false;

        hooks.OnKeyboardEvent(WM_KEYDOWN, 0x41, 0x1E, 0).ShouldBeFalse();
    }

    [Fact]
    public void OnKeyboardEvent_AltGrFakeCtrl_NotForwardedAndPassedOn()
    {
        using var hooks = new InputHooks();
        var calls = 0;
        hooks.KeyHandler = _ =>
        {
            calls++;
            return true;
        };

        hooks.OnKeyboardEvent(WM_KEYDOWN, 0xA2, 0x21D, 0).ShouldBeFalse();

        calls.ShouldBe(0);
    }

    [Fact]
    public void OnKeyboardEvent_NoHandler_PassesTheEventOn()
    {
        using var hooks = new InputHooks();

        hooks.OnKeyboardEvent(WM_KEYDOWN, 0x41, 0x1E, 0).ShouldBeFalse();
    }

    [Fact]
    public async Task OnKeyboardEvent_HandlerThrows_PassesTheEventOnAndLogsTheFirstFailureOnce()
    {
        var sink = new CapturingSink();
        using var hooks = new InputHooks(CreateLogger(sink));
        var failures = 0;
        hooks.KeyHandler = _ => throw new InvalidOperationException($"failure {++failures}");

        hooks.OnKeyboardEvent(WM_KEYDOWN, 0x41, 0x1E, 0).ShouldBeFalse();
        hooks.OnKeyboardEvent(WM_KEYUP, 0x41, 0x1E, 0).ShouldBeFalse();
        hooks.OnKeyboardEvent(WM_KEYDOWN, 0x41, 0x1E, 0).ShouldBeFalse();

        var events = await sink.WaitForEventsAsync();
        failures.ShouldBe(3);
        var logged = events.ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogEventLevel.Error);
        logged.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("failure 1");
        ((ScalarValue)logged.Properties["Hook"]).Value.ShouldBe("keyboard");
    }

    [Fact]
    public void OnMouseEvent_ButtonDown_CallsHandler()
    {
        using var hooks = new InputHooks();
        var calls = 0;
        hooks.MouseHandler = () => calls++;

        hooks.OnMouseEvent(WM_LBUTTONDOWN, 0);

        calls.ShouldBe(1);
    }

    [Fact]
    public void OnMouseEvent_InjectedButtonDown_DoesNotCallHandler()
    {
        using var hooks = new InputHooks();
        var calls = 0;
        hooks.MouseHandler = () => calls++;

        hooks.OnMouseEvent(WM_LBUTTONDOWN, LLMHF_INJECTED);

        calls.ShouldBe(0);
    }

    [Fact]
    public async Task OnMouseEvent_HandlerThrows_LogsTheFirstFailureOnce()
    {
        var sink = new CapturingSink();
        using var hooks = new InputHooks(CreateLogger(sink));
        var failures = 0;
        hooks.MouseHandler = () => throw new InvalidOperationException($"failure {++failures}");

        hooks.OnMouseEvent(WM_LBUTTONDOWN, 0);
        hooks.OnMouseEvent(WM_MOUSEWHEEL, 0);

        var events = await sink.WaitForEventsAsync();
        failures.ShouldBe(2);
        var logged = events.ShouldHaveSingleItem();
        logged.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("failure 1");
        ((ScalarValue)logged.Properties["Hook"]).Value.ShouldBe("mouse");
    }

    private static ILogger CreateLogger(CapturingSink sink) =>
        new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

    /// <summary>Collects the events of a private logger, which the hooks write to from the thread pool.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

        /// <summary>
        /// Waits for the first event, then a little longer so that a wrongly queued second one would arrive too.
        /// </summary>
        public async Task<LogEvent[]> WaitForEventsAsync()
        {
            var ct = TestContext.Current.CancellationToken;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_events.IsEmpty && DateTime.UtcNow < deadline)
                await Task.Delay(10, ct);

            await Task.Delay(200, ct);
            return [.. _events];
        }
    }
}
