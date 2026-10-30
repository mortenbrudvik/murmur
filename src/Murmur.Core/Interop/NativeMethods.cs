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

    // ---- Keyboard input (SendInput) ----

    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    /// <summary>The INPUT union. <see cref="mi"/> is the largest member, so it fixes the struct's native size.</summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static unsafe partial uint SendInput(uint cInputs, INPUT* pInputs, int cbSize);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    internal static partial uint MapVirtualKey(uint uCode, uint uMapType);

    // ---- Clipboard ----

    /// <param name="hWndNewOwner">0 associates the open clipboard with the calling thread instead of a window.</param>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EmptyClipboard();

    /// <returns>The next format, or 0 when there are no more (last error 0) or on failure (last error set).</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint EnumClipboardFormats(uint format);

    [LibraryImport("user32.dll")]
    internal static partial int CountClipboardFormats();

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint GetClipboardData(uint uFormat);

    /// <remarks>On success the system owns <paramref name="hMem"/>; on failure the caller must free it.</remarks>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterClipboardFormat(string lpszFormat);

    [LibraryImport("user32.dll")]
    internal static partial uint GetClipboardSequenceNumber();

    // ---- Global memory (clipboard data) ----

    internal const uint GMEM_MOVEABLE = 0x0002;
    internal const uint GMEM_ZEROINIT = 0x0040;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GlobalFree(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nuint GlobalSize(nint hMem);

    // ---- Enhanced metafiles (CF_ENHMETAFILE) ----

    /// <param name="lpData">Null to get the size in bytes.</param>
    /// <returns>The size of the metafile bits, or the number of bytes copied; 0 on failure.</returns>
    [LibraryImport("gdi32.dll")]
    internal static unsafe partial uint GetEnhMetaFileBits(nint hEMF, uint nSize, byte* lpData);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static unsafe partial nint SetEnhMetaFileBits(uint nSize, byte* pb);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteEnhMetaFile(nint hmf);

    // ---- Foreground window and process ----

    internal const int ERROR_ACCESS_DENIED = 5;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const int TokenElevation = 20;

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool QueryFullProcessImageName(nint hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    /// <summary>GetTokenInformation for a single-DWORD class such as <see cref="TokenElevation"/> (TOKEN_ELEVATION).</summary>
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, out uint tokenInformation,
        uint tokenInformationLength, out uint returnLength);

    // ---- Package identity ----

    /// <summary>
    /// <see cref="GetCurrentPackageFullName"/> answers with an error code rather than a BOOL: this one means the process
    /// has no package, anything else (including ERROR_INSUFFICIENT_BUFFER from a zero-length buffer) means it has one.
    /// </summary>
    internal const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    /// <summary>
    /// Called with a zero length and no buffer purely to read the status: ERROR_INSUFFICIENT_BUFFER when the process has
    /// a package, <see cref="APPMODEL_ERROR_NO_PACKAGE"/> when it does not. The name itself is never wanted, which is
    /// why the buffer is a pointer that is always null here.
    /// </summary>
    [LibraryImport("kernel32.dll")]
    internal static unsafe partial int GetCurrentPackageFullName(ref uint packageFullNameLength, char* packageFullName);

    // ---- DXGI (display adapters) ----

    /// <param name="riid">The IID of <see cref="IDXGIFactory1"/>.</param>
    [LibraryImport("dxgi.dll")]
    internal static partial int CreateDXGIFactory1(in Guid riid, out IDXGIFactory1 factory);
}
