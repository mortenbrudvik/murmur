using System.Runtime.InteropServices;

namespace Murmur.UI.Pill;

/// <summary>The few Win32 calls the pill needs: its extended window styles and the foreground monitor.</summary>
internal static partial class PillNative
{
    internal const int GWL_EXSTYLE = -20;

    /// <summary>Never becomes the foreground window (clicks and Show do not activate it).</summary>
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    /// <summary>A tool window: no taskbar button and not listed in Alt+Tab.</summary>
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    /// <summary>Mouse input falls through to the window below (click-through).</summary>
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_TOPMOST = 0x00000008;

    internal const int PillExStyles = WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_TOPMOST;

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint DefaultDpi = 96;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hWnd, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Adds the pill's extended styles (never-activate, tool window, click-through, topmost) to <paramref name="hwnd"/>.</summary>
    internal static void ApplyPillExStyles(nint hwnd) =>
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | PillExStyles);

    /// <summary>
    /// The work area (screen minus the taskbar) of the monitor holding the foreground window, in physical pixels,
    /// plus that monitor's DPI. Falls back to the nearest monitor, so a missing foreground window never fails.
    /// </summary>
    internal static bool TryGetForegroundWorkArea(out RECT workArea, out uint dpi)
    {
        workArea = default;
        dpi = DefaultDpi;

        var monitor = MonitorFromWindow(GetForegroundWindow(), MONITOR_DEFAULTTONEAREST);
        if (monitor == 0)
        {
            return false;
        }

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        workArea = info.rcWork;
        if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX != 0)
        {
            dpi = dpiX;
        }

        return true;
    }
}
