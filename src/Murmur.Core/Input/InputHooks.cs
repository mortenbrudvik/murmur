using System.ComponentModel;
using System.Runtime.InteropServices;
using Murmur.Core.Interop;
using Serilog;

namespace Murmur.Core.Input;

/// <summary>
/// Global low-level keyboard hook (always on once installed) and mouse hook (on demand, only while the hotkey is held,
/// so mouse movement is not routed through Murmur the rest of the time).
/// <para>
/// Install, use and dispose on one thread that runs a message loop (Murmur gives it a dedicated thread): low-level
/// hooks are called on the installing thread's message loop, and Windows silently removes a hook whose callback is
/// too slow. The callbacks therefore only translate the event, call the handler and return; no I/O, no logging, and
/// no exception is ever allowed to escape into native code. The first exception each callback catches is logged from
/// the thread pool, so a broken handler leaves a trace without slowing the hook down.
/// </para>
/// </summary>
public sealed class InputHooks : IDisposable
{
    private readonly ILogger _logger;

    // The delegates live in fields for the object's whole lifetime: native code holds function pointers to them,
    // so the GC must never collect them while a hook is installed.
    private readonly NativeMethods.LowLevelHookProc _keyboardProc;
    private readonly NativeMethods.LowLevelHookProc _mouseProc;
    private readonly nint _keyboardProcPointer;
    private readonly nint _mouseProcPointer;
    private readonly nint _module;

    private nint _keyboardHook;
    private nint _mouseHook;
    private bool _mouseHookFailureLogged;
    private bool _keyboardCallbackFailureLogged;
    private bool _mouseCallbackFailureLogged;
    private bool _disposed;

    public InputHooks()
        : this(Log.ForContext<InputHooks>())
    {
    }

    /// <summary>Creates the hooks with the given logger; for tests, which must not swap the global logger.</summary>
    internal InputHooks(ILogger logger)
    {
        _logger = logger;
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
        _keyboardProcPointer = Marshal.GetFunctionPointerForDelegate(_keyboardProc);
        _mouseProcPointer = Marshal.GetFunctionPointerForDelegate(_mouseProc);
        _module = NativeMethods.GetModuleHandle(null);
    }

    /// <summary>Called for every key down/up; return true to swallow the event.</summary>
    public Func<KeyInput, bool>? KeyHandler { get; set; }

    /// <summary>Called for every non-injected mouse button-down or wheel event while the mouse hook is enabled.</summary>
    public Action? MouseHandler { get; set; }

    /// <summary>Installs the keyboard hook (WH_KEYBOARD_LL). Idempotent.</summary>
    /// <exception cref="Win32Exception">The hook could not be installed.</exception>
    public void Install()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_keyboardHook != 0)
            return;

        var hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProcPointer, _module, 0);
        if (hook == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        _keyboardHook = hook;
        _logger.Debug("Keyboard hook installed");
    }

    /// <summary>
    /// Installs or removes the mouse hook (WH_MOUSE_LL). Idempotent, and never throws because it is called from
    /// within the keyboard hook; if the hook cannot be installed, mouse input simply goes unseen.
    /// </summary>
    public void SetMouseHookEnabled(bool enabled)
    {
        if (_disposed)
            return;

        if (enabled)
        {
            if (_mouseHook != 0)
                return;

            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProcPointer, _module, 0);
            if (_mouseHook == 0 && !_mouseHookFailureLogged)
            {
                // Logged once only: this runs on the keyboard hook's path.
                _mouseHookFailureLogged = true;
                _logger.Warning("Mouse hook could not be installed (Win32 error {Error})", Marshal.GetLastPInvokeError());
            }
        }
        else if (_mouseHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_mouseHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }

        if (_keyboardHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
    }

    private unsafe nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            var data = (NativeMethods.KBDLLHOOKSTRUCT*)lParam;
            if (OnKeyboardEvent((int)wParam, data->vkCode, data->scanCode, data->flags))
                return 1;
        }

        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private unsafe nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            var data = (NativeMethods.MSLLHOOKSTRUCT*)lParam;
            OnMouseEvent((int)wParam, data->flags);
        }

        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    /// <summary>
    /// The keyboard callback's body for one HC_ACTION event: translates it, calls <see cref="KeyHandler"/> and returns
    /// true to swallow the event. Never throws: if the handler throws, the event passes through untouched and the
    /// first such exception is logged.
    /// </summary>
    internal bool OnKeyboardEvent(int message, uint vkCode, uint scanCode, uint flags)
    {
        try
        {
            return KeyHandler is { } handler
                   && TryTranslateKey(message, vkCode, scanCode, flags, out var input)
                   && handler(input);
        }
        catch (Exception ex)
        {
            // Never unwind into native code. The event passes through untouched.
            if (!_keyboardCallbackFailureLogged)
            {
                _keyboardCallbackFailureLogged = true;
                QueueCallbackFailureLog(ex, "keyboard");
            }

            return false;
        }
    }

    /// <summary>
    /// The mouse callback's body for one HC_ACTION event: calls <see cref="MouseHandler"/> if
    /// <see cref="IsReportedMouseEvent"/> says so. Mouse events are never swallowed. Never throws: if the handler
    /// throws, the first such exception is logged.
    /// </summary>
    internal void OnMouseEvent(int message, uint flags)
    {
        try
        {
            if (MouseHandler is { } handler && IsReportedMouseEvent(message, flags))
                handler();
        }
        catch (Exception ex)
        {
            // Never unwind into native code.
            if (!_mouseCallbackFailureLogged)
            {
                _mouseCallbackFailureLogged = true;
                QueueCallbackFailureLog(ex, "mouse");
            }
        }
    }

    /// <summary>
    /// Translates a low-level keyboard message into the handler's input. False for anything but a key down or up
    /// (WM_KEYDOWN, WM_SYSKEYDOWN, WM_KEYUP, WM_SYSKEYUP) and for the synthetic Left Ctrl that precedes AltGr
    /// (<see cref="IsAltGrFakeCtrl"/>).
    /// </summary>
    internal static bool TryTranslateKey(int message, uint vkCode, uint scanCode, uint flags, out KeyInput input)
    {
        var isDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
        var isUp = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
        if ((!isDown && !isUp) || IsAltGrFakeCtrl(vkCode, scanCode))
        {
            input = default;
            return false;
        }

        var injected = (flags & NativeMethods.LLKHF_INJECTED) != 0;
        input = new KeyInput((int)vkCode, isDown, injected);
        return true;
    }

    /// <summary>
    /// Whether a low-level mouse message reaches the handler: a button down or wheel event that was not injected.
    /// </summary>
    internal static bool IsReportedMouseEvent(int message, uint flags) =>
        IsButtonDownOrWheel(message) && (flags & NativeMethods.LLMHF_INJECTED) == 0;

    /// <summary>Logs a callback failure from the thread pool, so the hook thread returns at once.</summary>
    private void QueueCallbackFailureLog(Exception exception, string hook)
    {
        try
        {
            var logger = _logger;
            ThreadPool.QueueUserWorkItem(_ => logger.Error(
                exception,
                "The {Hook} hook callback failed; the event was passed on, and further failures are not logged",
                hook));
        }
        catch (Exception)
        {
            // Nothing more can be done from inside a hook.
        }
    }

    /// <summary>
    /// On AltGr keyboard layouts (UK, Nordic, German, French, …) Windows precedes every Right Alt event with a
    /// synthetic Left Ctrl event (scan code 0x21D, not flagged as injected). It is not a real key press, so it is
    /// never forwarded to the handler: otherwise Right Alt could not be captured as a hotkey, and a Left Ctrl hotkey
    /// would fire on every AltGr character. The event itself still goes down the hook chain.
    /// </summary>
    internal static bool IsAltGrFakeCtrl(uint vkCode, uint scanCode) => vkCode == VirtualKeys.LeftCtrl && scanCode == 0x21D;

    private static bool IsButtonDownOrWheel(int message) => message is
        NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN
        or NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_MOUSEWHEEL or NativeMethods.WM_MOUSEHWHEEL;
}
