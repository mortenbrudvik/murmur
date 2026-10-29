using System.ComponentModel;
using System.Runtime.InteropServices;
using Murmur.Core.Input;
using Murmur.Core.Insertion;
using Murmur.Core.Models;
using Serilog;

namespace Murmur.UI;

/// <summary>
/// Runs Murmur's low-level keyboard and mouse hooks on a dedicated thread ("Murmur.InputHooks") with its own Win32
/// message loop, and routes their events: hotkey capture first, then the hotkey matcher, whose signals go to
/// <c>onSignal</c>.
/// <para>
/// Not on the WPF UI thread: low-level hooks are called on the installing thread's message loop, and Windows silently
/// removes a hook whose callback takes longer than LowLevelHooksTimeout. The UI thread can stall for that long
/// (opening a Bluetooth microphone, WebView2 work, a garbage collection); this thread only ever runs the callbacks.
/// </para>
/// </summary>
/// <param name="onSignal">
/// Receives every signal other than <see cref="HotkeySignal.None"/>, on the hook thread. It must return at once, so
/// it should only post the signal elsewhere.
/// </param>
public sealed partial class InputHookHost(
    HotkeyMatcher matcher, HotkeyCaptureCoordinator coordinator, IKeyboardSender keyboard, Action<HotkeySignal> onSignal)
    : IDisposable
{
    private const string ThreadName = "Murmur.InputHooks";
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger _logger = Log.ForContext<InputHookHost>();

    private Thread? _thread;
    private uint _threadId;

    // Created, used and disposed on the hook thread only.
    private InputHooks? _hooks;
    private bool _disposed;

    /// <summary>True while the hook thread (and so its message loop) is running.</summary>
    internal bool IsRunning => _thread?.IsAlive == true;

    /// <summary>Starts the hook thread and waits until the keyboard hook is installed.</summary>
    /// <exception cref="Win32Exception">The keyboard hook could not be installed.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null)
            return;

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(started))
        {
            Name = ThreadName,
            IsBackground = true,
            // Keeps the callbacks prompt while the CPU is busy (for example a transcription on the CPU fallback).
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();

        if (!started.Task.Wait(StartTimeout))
            throw new TimeoutException("The input hook thread did not start in time.");

        // Rethrows the installation failure, if any, on the caller's thread.
        started.Task.GetAwaiter().GetResult();
    }

    /// <summary>Removes the hooks on their own thread, ends its message loop and waits briefly for it to exit.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var thread = _thread;
        if (thread is null || !thread.IsAlive)
            return;

        if (!Native.PostThreadMessage(_threadId, Native.WM_QUIT, 0, 0))
        {
            _logger.Warning("Could not stop the input hook thread (Win32 error {Error})", Marshal.GetLastPInvokeError());
            return;
        }

        if (!thread.Join(StopTimeout))
            _logger.Warning("The input hook thread did not stop within {Timeout}", StopTimeout);
    }

    /// <summary>Hook thread: a key went down or up. Returns true to keep it from reaching other apps.</summary>
    internal bool OnKey(KeyInput key) => coordinator.TryHandle(key) || Route(matcher.OnKey(key));

    /// <summary>Hook thread: a mouse button went down or a wheel turned while the hotkey was held.</summary>
    internal void OnMouse() => Route(matcher.OnMouseButtonOrWheel());

    /// <summary>
    /// A lone Alt or Win key press, released with nothing in between, opens the focused app's menu bar or the Start
    /// menu, which would take focus away from the app being dictated into.
    /// </summary>
    internal static bool NeedsMenuMask(HotkeyDefinition hotkey) =>
        hotkey.IsModifierOnly && hotkey.VirtualKey is 0xA4 or 0xA5 or 0x5B or 0x5C;

    private bool Route(MatchResult result)
    {
        // The mouse hook is only installed while the hotkey is held, so mouse movement is not routed through Murmur
        // the rest of the time. Same thread as the keyboard hook, which is where it must be installed.
        _hooks?.SetMouseHookEnabled(matcher.IsHotkeyHeld);

        if (result.Signal == HotkeySignal.None)
            return result.Swallow;

        if (result.Signal == HotkeySignal.HotkeyDown && NeedsMenuMask(matcher.Hotkey))
        {
            // Queued, never sent from inside the hook callback: SendInput there would delay the callback's return.
            ThreadPool.QueueUserWorkItem(static sender => sender.SendMaskKey(), keyboard, preferLocal: false);
        }

        onSignal(result.Signal);
        return result.Swallow;
    }

    private void Run(TaskCompletionSource started)
    {
        _threadId = Native.GetCurrentThreadId();

        // Creates this thread's message queue before anyone can post WM_QUIT to it.
        Native.PeekMessage(out _, 0, 0, 0, Native.PM_NOREMOVE);

        using var hooks = new InputHooks();
        hooks.KeyHandler = OnKey;
        hooks.MouseHandler = OnMouse;
        try
        {
            hooks.Install();
        }
        catch (Exception ex)
        {
            started.SetException(ex);
            return;
        }

        _hooks = hooks;
        _logger.Information("Input hooks installed on thread {ThreadId}", _threadId);
        started.SetResult();

        // GetMessage returns 0 for WM_QUIT and -1 on failure.
        while (Native.GetMessage(out var message, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(in message);
            Native.DispatchMessage(in message);
        }

        _hooks = null;
        _logger.Information("Input hooks removed");
    }

    private static partial class Native
    {
        internal const uint WM_QUIT = 0x0012;
        internal const uint PM_NOREMOVE = 0x0000;

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSG
        {
            public nint hwnd;
            public uint message;
            public nint wParam;
            public nint lParam;
            public uint time;
            public int ptX;
            public int ptY;
            public uint lPrivate;
        }

        [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
        internal static partial int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TranslateMessage(in MSG lpMsg);

        [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
        internal static partial nint DispatchMessage(in MSG lpMsg);

        [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

        [LibraryImport("kernel32.dll")]
        internal static partial uint GetCurrentThreadId();
    }
}
