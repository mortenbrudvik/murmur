using System.ComponentModel;
using System.Runtime.InteropServices;
using Murmur.Core.Interop;
using Serilog;

namespace Murmur.Core.Input;

/// <summary>
/// Global low-level keyboard hook (always on once installed) and mouse hook (on demand, only while the hotkey is held,
/// so mouse movement is not routed through Murmur the rest of the time).
/// <para>
/// Install and use on the UI thread only: low-level hooks are called on the installing thread's message loop, and
/// Windows silently removes a hook whose callback is too slow. The callbacks therefore only translate the event, call
/// the handler and return; no I/O, no logging, and no exception is ever allowed to escape into native code.
/// </para>
/// </summary>
public sealed class InputHooks : IDisposable
{
    private readonly ILogger _logger = Log.ForContext<InputHooks>();

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
    private bool _disposed;

    public InputHooks()
    {
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
        try
        {
            if (nCode == NativeMethods.HC_ACTION && KeyHandler is { } handler)
            {
                var message = (int)wParam;
                var isDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                if (isDown || message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                {
                    var data = (NativeMethods.KBDLLHOOKSTRUCT*)lParam;
                    var injected = (data->flags & NativeMethods.LLKHF_INJECTED) != 0;
                    if (handler(new KeyInput((int)data->vkCode, isDown, injected)))
                        return 1;
                }
            }
        }
        catch (Exception)
        {
            // Never unwind into native code. The event passes through untouched.
        }

        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private unsafe nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode == NativeMethods.HC_ACTION && MouseHandler is { } handler && IsButtonDownOrWheel((int)wParam))
            {
                var data = (NativeMethods.MSLLHOOKSTRUCT*)lParam;
                if ((data->flags & NativeMethods.LLMHF_INJECTED) == 0)
                    handler();
            }
        }
        catch (Exception)
        {
            // Never unwind into native code. Mouse events are never swallowed.
        }

        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private static bool IsButtonDownOrWheel(int message) => message is
        NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN
        or NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_MOUSEWHEEL or NativeMethods.WM_MOUSEHWHEEL;
}
