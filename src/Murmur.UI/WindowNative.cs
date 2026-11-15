using System.Runtime.InteropServices;

namespace Murmur.UI;

/// <summary>The Win32 calls behind the splash's hand-over: hiding a shown window from view, and asking who has the foreground.</summary>
internal static partial class WindowNative
{
    /// <summary>
    /// DWMWA_CLOAK: the window stays shown as far as Windows and WPF are concerned (it lays out, its WebView loads and
    /// paints), but the desktop compositor does not draw it. It does have its taskbar button (measured).
    /// </summary>
    private const uint DWMWA_CLOAK = 13;

    /// <summary>Cloaks or uncloaks a window. False when the compositor refused (it never does on Windows 11).</summary>
    internal static bool SetCloaked(nint window, bool cloaked)
    {
        var value = cloaked ? 1 : 0;
        return DwmSetWindowAttribute(window, DWMWA_CLOAK, ref value, sizeof(int)) == 0;
    }

    /// <summary>The process that owns the foreground window; 0 when no window has the foreground.</summary>
    internal static uint ForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == 0)
            return 0;

        _ = GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);
}
