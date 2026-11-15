using System.Runtime.InteropServices;

namespace Murmur.UI;

/// <summary>The Win32 call behind the splash's hand-over: hiding a shown window from view.</summary>
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

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, int size);
}
