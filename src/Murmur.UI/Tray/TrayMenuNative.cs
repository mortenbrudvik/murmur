using System.Windows;
using Murmur.UI.Native;

namespace Murmur.UI.Tray;

/// <summary>
/// Where the tray menu goes: where the mouse is, which monitor that is on, and where the menu's window is. Everything
/// is in the screen's own pixels, which is what makes it right on a monitor that is scaled.
/// </summary>
internal static class TrayMenuNative
{
    /// <summary>Where the mouse is; null when Windows will not say.</summary>
    internal static Point? CursorPosition() => User32.GetCursorPos(out var point) ? new Point(point.x, point.y) : null;

    /// <summary>The whole area of the monitor nearest to <paramref name="point"/>; null when it cannot be read.</summary>
    internal static Rect? MonitorBounds(Point point) =>
        User32.MonitorInfo(User32.MonitorFromPoint(
            new User32.POINT { x = (int)point.X, y = (int)point.Y }, User32.MONITOR_DEFAULTTONEAREST))?.rcMonitor.ToRect();

    /// <summary>
    /// The bottom-right corner of the primary monitor's work area, which is where the notification area is on a
    /// taskbar in its usual place: where a menu opened from the keyboard goes. Null when it cannot be read.
    /// </summary>
    internal static Point? NotificationAreaCorner() =>
        User32.MonitorInfo(User32.MonitorFromPoint(default, User32.MONITOR_DEFAULTTOPRIMARY)) is { } info
            ? new Point(info.rcWork.right, info.rcWork.bottom)
            : null;

    /// <summary>Where the window is on the screen; null when it cannot be read.</summary>
    internal static Rect? WindowBounds(nint window) => User32.GetWindowRect(window, out var rect) ? rect.ToRect() : null;
}
