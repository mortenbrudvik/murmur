using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Murmur.UI.Tray;

/// <summary>
/// Puts the tray menu next to the icon. H.NotifyIcon hands WPF the click's position for the menu, converted with a
/// scale that is wrong when the monitors are scaled differently (measured on the owner's PC: a 150 % monitor next to a
/// 100 % one), and WPF then pushes the menu into a corner of the screen, away from the icon. So once the menu is
/// open its window is moved here, in the screen's own pixels, which need no conversion: it opens upwards from the
/// click with the corner of the visible menu on it, as Windows' own tray menus do.
/// </summary>
internal static class TrayMenuPlacement
{
    /// <summary>
    /// Moves the open menu's window so that the visible menu's bottom-left corner is on <paramref name="anchor"/>,
    /// a point in the screen's own pixels. False when there is nothing to move: the menu is not open or not laid out.
    /// </summary>
    internal static bool TryPlace(ContextMenu menu, Point anchor)
    {
        if (PresentationSource.FromVisual(menu) is not HwndSource { CompositionTarget: { } target, RootVisual: { } root } source)
            return false;

        menu.UpdateLayout();
        var painted = VisibleBounds(menu);
        if (painted.IsEmpty || TrayMenuNative.MonitorBounds(anchor) is not { } monitor)
            return false;

        // The painted part within the menu's window, in pixels: the window has no frame of its own.
        painted.Offset(LayoutOffset(menu, root));
        var visible = Rect.Transform(painted, target.TransformToDevice);
        var origin = WindowOrigin(anchor, visible, monitor);
        return TrayMenuNative.MoveTo(source.Handle, (int)Math.Round(origin.X), (int)Math.Round(origin.Y));
    }

    /// <summary>
    /// The part of the menu that is painted, in the menu's own coordinates: the first border with a background in its
    /// template. A theme draws the menu inside a margin that holds its shadow, and it is the menu that is seen, not
    /// that margin, that belongs next to the icon. The whole menu when nothing in it is painted that way; empty for a
    /// menu that has not been laid out.
    /// <para>
    /// It is where the layout puts the border, not where it is drawn right now: the theme slides the menu in from
    /// above as it opens, and the menu is placed during that animation.
    /// </para>
    /// </summary>
    internal static Rect VisibleBounds(ContextMenu menu)
    {
        if (menu.ActualWidth <= 0 || menu.ActualHeight <= 0)
            return Rect.Empty;

        return Painted(menu) is { } border
            ? new Rect((Point)LayoutOffset(border, menu), border.RenderSize)
            : new Rect(0, 0, menu.ActualWidth, menu.ActualHeight);
    }

    /// <summary>
    /// How far <paramref name="element"/> is laid out from <paramref name="ancestor"/>'s corner. Render transforms,
    /// which is what animations move things with, are left out.
    /// </summary>
    private static Vector LayoutOffset(Visual element, Visual ancestor)
    {
        var offset = default(Vector);
        for (DependencyObject? visual = element; visual is not null && !ReferenceEquals(visual, ancestor); visual = VisualTreeHelper.GetParent(visual))
        {
            if (visual is Visual part)
                offset += VisualTreeHelper.GetOffset(part);
        }

        return offset;
    }

    /// <summary>
    /// Where the menu window's top-left corner goes, all in the screen's own pixels. <paramref name="visible"/> is the
    /// painted part within the window. The painted part's bottom-left corner goes on <paramref name="anchor"/>, so the
    /// menu opens upwards; downwards when there is no room above (a taskbar at the top). The painted part is kept on
    /// <paramref name="monitor"/>; the shadow margin around it may hang over the edge.
    /// </summary>
    internal static Point WindowOrigin(Point anchor, Rect visible, Rect monitor)
    {
        var x = anchor.X - visible.Left;
        var y = anchor.Y - visible.Bottom;
        if (y + visible.Top < monitor.Top)
            y = anchor.Y - visible.Top;

        x = Math.Min(x, monitor.Right - visible.Right);
        x = Math.Max(x, monitor.Left - visible.Left);
        y = Math.Min(y, monitor.Bottom - visible.Bottom);
        y = Math.Max(y, monitor.Top - visible.Top);
        return new Point(x, y);
    }

    private static Border? Painted(DependencyObject parent)
    {
        var children = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < children; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            // The menu's own frame is looked for, not the items in it: they have borders of their own.
            if (child is ItemsPresenter or MenuItem or Separator)
                continue;

            if (child is Border { ActualWidth: > 0, ActualHeight: > 0 } border && IsPainted(border.Background))
                return border;

            if (Painted(child) is { } deeper)
                return deeper;
        }

        return null;
    }

    /// <summary>A brush that shows: not missing, and not a see-through colour put there only to take mouse clicks.</summary>
    private static bool IsPainted(Brush? brush) => brush switch
    {
        null => false,
        SolidColorBrush solid => solid.Color.A > 0,
        _ => true,
    };
}
