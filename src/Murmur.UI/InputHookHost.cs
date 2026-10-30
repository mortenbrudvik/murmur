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
/// <para>
/// Not sealed, with <see cref="Start"/>, <see cref="Dispose"/> and <see cref="Install"/> virtual, only so that
/// <see cref="InputHookStarter"/> and the race between a slow install and a Dispose can be tested without installing
/// a real hook.
/// </para>
/// </summary>
/// <param name="onSignal">
/// Receives every signal other than <see cref="HotkeySignal.None"/>, on the hook thread, with the time the hook saw it
/// (from <paramref name="time"/>), so tap and hold are timed from the key events themselves, not from whenever the
/// UI thread handles them. It must return at once, so it should only post the signal elsewhere.
/// </param>
public partial class InputHookHost(
    HotkeyMatcher matcher, HotkeyCaptureCoordinator coordinator, IKeyboardSender keyboard, TimeProvider time,
    Action<HotkeySignal, DateTimeOffset> onSignal)
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

    // Written by Dispose on the caller's thread, read by the hook thread (see Run).
    private volatile bool _disposed;

    /// <summary>True while the hook thread (and so its message loop) is running.</summary>
    internal bool IsRunning => _thread?.IsAlive == true;

    /// <summary>True once <see cref="Dispose"/> was called.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>Starts the hook thread and waits until the keyboard hook is installed.</summary>
    /// <exception cref="Win32Exception">The keyboard hook could not be installed.</exception>
    public virtual void Start()
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

        bool completed;
        try
        {
            completed = started.Task.Wait(StartTimeout);
        }
        catch (AggregateException)
        {
            // Wait wraps a failure in an AggregateException; GetResult below rethrows the failure itself.
            completed = true;
        }

        if (!completed)
            throw new TimeoutException("The input hook thread did not start in time.");

        // Rethrows the installation failure, if any, on the caller's thread.
        started.Task.GetAwaiter().GetResult();
    }

    /// <summary>Removes the hooks on their own thread, ends its message loop and waits briefly for it to exit.</summary>
    public virtual void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var thread = _thread;
        if (thread is null || !thread.IsAlive)
            return;

        // If the thread has not got as far as its message queue yet, this fails; Run then sees _disposed (set above)
        // once its hooks are installed and ends itself.
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
        var at = time.GetUtcNow();

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

        onSignal(result.Signal, at);
        return result.Swallow;
    }

    /// <summary>Installs the keyboard hook on the hook thread. Virtual for the same reason as <see cref="Start"/>.</summary>
    protected virtual void Install(InputHooks hooks) => hooks.Install();

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
            Install(hooks);
        }
        catch (Exception ex)
        {
            started.SetException(ex);
            return;
        }

        // Disposed while the hook was being installed (a start that timed out and was given up on, or a Dispose that
        // came before this thread could receive its WM_QUIT): do not keep the hook, or it would run beside the next
        // host's. Leaving here removes it, through the using above.
        if (_disposed)
        {
            _logger.Information("The input hook host was disposed while its hooks were being installed; removing them");
            started.TrySetCanceled();
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
