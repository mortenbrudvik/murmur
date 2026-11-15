using System.Windows;
using System.Windows.Media;

namespace Murmur.UI.Splash;

/// <summary>
/// The icon's colours, for drawing its tile and line outside the icon file: on the splash and in the About window's
/// header. They are the artwork's own (murmur-icon.svg), not theme colours.
/// </summary>
internal static class HumPaint
{
    /// <summary>The tile: blue to violet from its top-left corner to its bottom-right.</summary>
    internal static readonly Brush TileBrush = Frozen(new LinearGradientBrush(
        [
            new GradientStop(Color.FromRgb(0x56, 0xA0, 0xFF), 0),
            new GradientStop(Color.FromRgb(0x7A, 0x78, 0xFF), 0.55),
            new GradientStop(Color.FromRgb(0xAE, 0x62, 0xF2), 1),
        ],
        new Point(0, 0), new Point(1, 1)));

    /// <summary>The glow behind the tile.</summary>
    internal static readonly Brush GlowBrush = Frozen(new RadialGradientBrush(
        Color.FromArgb(0x80, 0x7A, 0x78, 0xFF), Color.FromArgb(0x00, 0x7A, 0x78, 0xFF)));

    /// <summary>The line outside the tile: periwinkle to violet across the drawing, fading to nothing at both edges.</summary>
    internal static Pen LinePen(HumFrame frame) => RoundPen(
        Absolute(new LinearGradientBrush(
            [
                new GradientStop(Color.FromArgb(0x00, 0x8F, 0xB3, 0xFF), 0),
                new GradientStop(Color.FromRgb(0x8F, 0xB3, 0xFF), 0.14),
                new GradientStop(Color.FromRgb(0xB5, 0x9B, 0xFF), 0.86),
                new GradientStop(Color.FromArgb(0x00, 0xB5, 0x9B, 0xFF), 1),
            ],
            new Point(0, 0), new Point(frame.Width, 0))),
        frame.StrokeWidth);

    /// <summary>The line inside the tile: white, a little faint towards the tile's sides, as on the icon.</summary>
    internal static Pen LitPen(HumFrame frame) => RoundPen(
        Absolute(new LinearGradientBrush(
            [
                new GradientStop(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Colors.White, 0.24),
                new GradientStop(Colors.White, 0.76),
                new GradientStop(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF), 1),
            ],
            new Point(frame.TileX, 0), new Point(frame.TileX + frame.TileSize, 0))),
        frame.StrokeWidth);

    /// <summary>The points as one open stroke.</summary>
    internal static StreamGeometry Line(Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            context.PolyLineTo(points.AsSpan(1).ToArray(), isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }

    internal static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Pen RoundPen(Brush brush, double width) => Frozen(new Pen(brush, width)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    });

    /// <summary>A gradient laid out in the drawing's own coordinates, not stretched over whatever it paints.</summary>
    private static LinearGradientBrush Absolute(LinearGradientBrush brush)
    {
        brush.MappingMode = BrushMappingMode.Absolute;
        return Frozen(brush);
    }
}
