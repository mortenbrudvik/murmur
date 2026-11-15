using System.Runtime.InteropServices;
using System.Windows;

namespace Murmur.UI.Native;

/// <summary>
/// The user32 calls the app's own windows share: where monitors and windows are, and moving a window. Everything is in
/// the screen's own pixels. Calls only one window needs stay next to it (PillNative, WindowNative).
/// </summary>
internal static partial class User32
{
    internal const uint MONITOR_DEFAULTTOPRIMARY = 1;
    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    internal const int SM_CXSMICON = 49;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;

        public readonly Rect ToRect() => new(left, top, right - left, bottom - top);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// Moves <paramref name="window"/>'s top-left corner to (<paramref name="x"/>, <paramref name="y"/>) in physical
    /// pixels, without resizing, reordering or activating it.
    /// </summary>
    internal static bool MoveTo(nint window, int x, int y) =>
        SetWindowPos(window, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    /// <summary>The monitor's whole area and work area; null when <paramref name="monitor"/> is 0 or cannot be read.</summary>
    internal static MONITORINFO? MonitorInfo(nint monitor)
    {
        if (monitor == 0)
            return null;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(monitor, ref info) ? info : null;
    }

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static partial int GetWindowLong(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    internal static partial int SetWindowLong(nint window, int index, int newLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint window, out RECT rect);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);
}
