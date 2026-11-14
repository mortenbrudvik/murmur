using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Murmur.UI.Splash;

/// <summary>
/// Draws the splash's animation for one moment in time: the line (a speech waveform that settles into the icon's m),
/// the tile lighting up around it, and the name being typed. <see cref="Time"/> is the only input; the window animates
/// it from 0 to <see cref="SplashTimeline.End"/>. With Windows' animations turned off it shows the final frame at once.
/// </summary>
internal sealed class SplashCanvas : FrameworkElement
{
    public static readonly DependencyProperty TimeProperty = DependencyProperty.Register(
        nameof(Time), typeof(double), typeof(SplashCanvas),
        new FrameworkPropertyMetadata(SplashTimeline.End, FrameworkPropertyMetadataOptions.AffectsRender, null, CoerceTime));

    private const int PointCount = 320;
    private const double NameTop = 164;
    private const double NameSize = 30;
    private const double CaretWidth = 2;
    /// <summary>How much of the line outside the tile is left once the tile is lit.</summary>
    private const double DimmedLine = 0.38;

    private static readonly Rect Tile = new(HumGeometry.TileX, HumGeometry.TileY, HumGeometry.TileSize, HumGeometry.TileSize);
    private static readonly Point TileCentre = new(Tile.X + Tile.Width / 2, Tile.Y + Tile.Height / 2);
    private static readonly Geometry TileClip = Frozen(new RectangleGeometry(Tile, HumGeometry.CornerRadius, HumGeometry.CornerRadius));

    private static readonly Brush TileBrush = Frozen(new LinearGradientBrush(
        [
            new GradientStop(Color.FromRgb(0x56, 0xA0, 0xFF), 0),
            new GradientStop(Color.FromRgb(0x7A, 0x78, 0xFF), 0.55),
            new GradientStop(Color.FromRgb(0xAE, 0x62, 0xF2), 1),
        ],
        new Point(0, 0), new Point(1, 1)));

    private static readonly Brush GlowBrush = Frozen(new RadialGradientBrush(
        Color.FromArgb(0x80, 0x7A, 0x78, 0xFF), Color.FromArgb(0x00, 0x7A, 0x78, 0xFF)));

    /// <summary>The line outside the tile: periwinkle to violet across the splash, fading to nothing at both edges.</summary>
    private static readonly Pen LinePen = RoundPen(Absolute(new LinearGradientBrush(
        [
            new GradientStop(Color.FromArgb(0x00, 0x8F, 0xB3, 0xFF), 0),
            new GradientStop(Color.FromRgb(0x8F, 0xB3, 0xFF), 0.14),
            new GradientStop(Color.FromRgb(0xB5, 0x9B, 0xFF), 0.86),
            new GradientStop(Color.FromArgb(0x00, 0xB5, 0x9B, 0xFF), 1),
        ],
        new Point(0, 0), new Point(HumGeometry.Width, 0))));

    /// <summary>The line inside the tile: white, a little faint towards the tile's sides, as on the icon.</summary>
    private static readonly Pen LitPen = RoundPen(Absolute(new LinearGradientBrush(
        [
            new GradientStop(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF), 0),
            new GradientStop(Colors.White, 0.24),
            new GradientStop(Colors.White, 0.76),
            new GradientStop(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF), 1),
        ],
        new Point(Tile.Left, 0), new Point(Tile.Right, 0))));

    private static readonly Brush NameBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE9, 0xEC, 0xF5)));
    private static readonly Brush CaretBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8F, 0xB3, 0xFF)));
    private static readonly Typeface NameTypeface = new(
        new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly Point[] _rest = HumGeometry.RestPoints(PointCount);
    private readonly Point[] _points = new Point[PointCount];

    public SplashCanvas()
    {
        Width = HumGeometry.Width;
        Height = HumGeometry.Height;
        IsHitTestVisible = false;
    }

    /// <summary>Seconds since the splash appeared.</summary>
    public double Time
    {
        get => (double)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    /// <summary>With animations turned off in Windows, every moment is the last one.</summary>
    private static object CoerceTime(DependencyObject d, object value) =>
        SystemParameters.ClientAreaAnimation ? value : SplashTimeline.End;

    protected override void OnRender(DrawingContext dc)
    {
        var time = Time;
        var reveal = SplashTimeline.TileReveal(time);
        HumGeometry.PointsAt(time, _rest, _points);
        var line = Line(_points);

        if (reveal > 0)
        {
            dc.PushOpacity(reveal);
            var glow = HumGeometry.TileSize * 1.35;
            dc.DrawEllipse(GlowBrush, null, TileCentre, glow, glow);
            dc.Pop();
        }

        dc.PushOpacity(1 - (1 - DimmedLine) * reveal);
        dc.DrawGeometry(null, LinePen, line);
        dc.Pop();

        if (reveal > 0)
        {
            dc.PushOpacity(reveal);

            // The tile grows the last tenth of its size as it lights up.
            var scale = 0.9 + 0.1 * reveal;
            dc.PushTransform(new ScaleTransform(scale, scale, TileCentre.X, TileCentre.Y));
            dc.DrawRoundedRectangle(TileBrush, null, Tile, HumGeometry.CornerRadius, HumGeometry.CornerRadius);
            dc.Pop();

            dc.PushClip(TileClip);
            dc.DrawGeometry(null, LitPen, line);
            dc.Pop();

            dc.Pop();
        }

        DrawName(dc, time);
    }

    /// <summary>The name, typed letter by letter where the whole word will stand, with the caret after the last letter.</summary>
    private void DrawName(DrawingContext dc, double time)
    {
        var typed = SplashTimeline.TypedLetters(time);
        var caret = SplashTimeline.CaretVisible(time);
        if (typed == 0 && !caret)
            return;

        var whole = Text(SplashTimeline.Word);
        var left = (HumGeometry.Width - whole.WidthIncludingTrailingWhitespace) / 2;
        var typedWidth = 0.0;
        if (typed > 0)
        {
            var text = Text(SplashTimeline.Word[..typed]);
            dc.DrawText(text, new Point(left, NameTop));
            typedWidth = text.WidthIncludingTrailingWhitespace;
        }

        if (caret)
        {
            dc.DrawRoundedRectangle(
                CaretBrush, null, new Rect(left + typedWidth + 1.5, NameTop + whole.Height * 0.14, CaretWidth, whole.Height * 0.78), 1, 1);
        }
    }

    private FormattedText Text(string text) => new(
        text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NameTypeface, NameSize, NameBrush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static StreamGeometry Line(Point[] points)
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

    private static Pen RoundPen(Brush brush) => Frozen(new Pen(brush, HumGeometry.StrokeWidth)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    });

    /// <summary>A gradient laid out in the splash's own coordinates, not stretched over whatever it paints.</summary>
    private static LinearGradientBrush Absolute(LinearGradientBrush brush)
    {
        brush.MappingMode = BrushMappingMode.Absolute;
        return Frozen(brush);
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
