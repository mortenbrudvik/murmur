using System.Windows;

namespace Murmur.UI.Pill;

public static class PillPlacement
{
    /// <summary>
    /// Top-left position of the pill: horizontally centered in the work area, resting <paramref name="bottomMarginDip"/>
    /// above its bottom edge (the top of the taskbar). Everything is in device-independent pixels.
    /// </summary>
    public static Point Compute(Rect workAreaDip, Size pillDip, double bottomMarginDip = 12) =>
        new(
            workAreaDip.Left + (workAreaDip.Width - pillDip.Width) / 2,
            workAreaDip.Bottom - pillDip.Height - bottomMarginDip);
}
