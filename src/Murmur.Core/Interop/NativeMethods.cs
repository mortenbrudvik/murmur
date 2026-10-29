using System.Runtime.InteropServices;

namespace Murmur.Core.Interop;

/// <summary>All Win32 P/Invoke declarations used by Murmur.Core.</summary>
internal static partial class NativeMethods
{
    // ---- Low-level keyboard and mouse hooks ----

    internal const int WH_KEYBOARD_LL = 13;
    internal const int WH_MOUSE_LL = 14;
    internal const int HC_ACTION = 0;

    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;

    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_RBUTTONDOWN = 0x0204;
    internal const int WM_MBUTTONDOWN = 0x0207;
    internal const int WM_MOUSEWHEEL = 0x020A;
    internal const int WM_XBUTTONDOWN = 0x020B;
    internal const int WM_MOUSEHWHEEL = 0x020E;

    /// <summary><see cref="KBDLLHOOKSTRUCT.flags"/>: the event was injected (SendInput).</summary>
    internal const uint LLKHF_INJECTED = 0x10;

    /// <summary><see cref="MSLLHOOKSTRUCT.flags"/>: the event was injected (SendInput).</summary>
    internal const uint LLMHF_INJECTED = 0x01;

    /// <summary>LowLevelKeyboardProc / LowLevelMouseProc.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint LowLevelHookProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public int ptX;
        public int ptY;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    /// <param name="lpfn">A function pointer from <see cref="Marshal.GetFunctionPointerForDelegate{TDelegate}(TDelegate)"/>;
    /// the caller must keep the delegate alive for as long as the hook is installed.</param>
    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static partial nint SetWindowsHookEx(int idHook, nint lpfn, nint hmod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    internal static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? lpModuleName);

    // ---- Keyboard state ----

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);
}
