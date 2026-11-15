using System.Runtime.InteropServices;
using System.Windows;

namespace Murmur.UI.Tray;

/// <summary>
/// The Win32 calls that place the tray menu: where the mouse is, which monitor that is on, and moving the menu's
/// window. Everything is in the screen's own pixels, which is what makes it right on a monitor that is scaled.
/// </summary>
internal static partial class TrayMenuNative
{
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;

        public readonly Rect ToRect() => new(left, top, right - left, bottom - top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>Where the mouse is; null when Windows will not say.</summary>
    internal static Point? CursorPosition() => GetCursorPos(out var point) ? new Point(point.x, point.y) : null;

    /// <summary>The whole area of the monitor nearest to <paramref name="point"/>; null when it cannot be read.</summary>
    internal static Rect? MonitorBounds(Point point) =>
        Monitor(MonitorFromPoint(new POINT { x = (int)point.X, y = (int)point.Y }, MONITOR_DEFAULTTONEAREST))?.rcMonitor.ToRect();

    /// <summary>
    /// The bottom-right corner of the primary monitor's work area, which is where the notification area is on a
    /// taskbar in its usual place: where a menu opened from the keyboard goes. Null when it cannot be read.
    /// </summary>
    internal static Point? NotificationAreaCorner() =>
        Monitor(MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY)) is { } info ? new Point(info.rcWork.right, info.rcWork.bottom) : null;

    /// <summary>Where the window is on the screen; null when it cannot be read.</summary>
    internal static Rect? WindowBounds(nint window) => GetWindowRect(window, out var rect) ? rect.ToRect() : null;

    /// <summary>Moves the window's top-left corner, without resizing, reordering or activating it.</summary>
    internal static bool MoveTo(nint window, int x, int y) =>
        SetWindowPos(window, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    private static MONITORINFO? Monitor(nint monitor)
    {
        if (monitor == 0)
            return null;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(monitor, ref info) ? info : null;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);
}
