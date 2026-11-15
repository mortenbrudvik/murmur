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

    public static readonly DependencyProperty NameBrushProperty = DependencyProperty.Register(
        nameof(NameBrush), typeof(Brush), typeof(SplashCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CaretBrushProperty = DependencyProperty.Register(
        nameof(CaretBrush), typeof(Brush), typeof(SplashCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const int PointCount = 320;
    private const double NameTop = 164;
    private const double NameSize = 30;
    private const double CaretWidth = 2;

    private static readonly HumTile Tile = new(HumGeometry.Splash);
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

    /// <summary>Seconds since the splash's animation began.</summary>
    public double Time
    {
        get => (double)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    /// <summary>The name's colour. Without one the name is not drawn.</summary>
    public Brush? NameBrush
    {
        get => (Brush?)GetValue(NameBrushProperty);
        set => SetValue(NameBrushProperty, value);
    }

    /// <summary>The caret's colour. Without one the caret is not drawn.</summary>
    public Brush? CaretBrush
    {
        get => (Brush?)GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    /// <summary>With animations turned off in Windows, every moment is the last one.</summary>
    private static object CoerceTime(DependencyObject d, object value) =>
        SystemParameters.ClientAreaAnimation ? value : SplashTimeline.End;

    protected override void OnRender(DrawingContext dc)
    {
        var time = Time;
        var reveal = SplashTimeline.TileReveal(time);
        HumGeometry.PointsAt(time, _rest, _points);
        Tile.Draw(dc, HumPaint.Line(_points), reveal);
        DrawName(dc, time);
    }

    /// <summary>The name, typed letter by letter where the whole word will stand, with the caret after the last letter.</summary>
    private void DrawName(DrawingContext dc, double time)
    {
        if (NameBrush is not { } nameBrush)
            return;

        var typed = SplashTimeline.TypedLetters(time);
        var caret = SplashTimeline.CaretVisible(time) && CaretBrush != null;
        if (typed == 0 && !caret)
            return;

        var whole = Text(SplashTimeline.Word, nameBrush);
        var left = (HumGeometry.Width - whole.WidthIncludingTrailingWhitespace) / 2;
        var typedWidth = 0.0;
        if (typed > 0)
        {
            var text = Text(SplashTimeline.Word[..typed], nameBrush);
            dc.DrawText(text, new Point(left, NameTop));
            typedWidth = text.WidthIncludingTrailingWhitespace;
        }

        if (caret)
        {
            dc.DrawRoundedRectangle(
                CaretBrush, null, new Rect(left + typedWidth + 1.5, NameTop + whole.Height * 0.14, CaretWidth, whole.Height * 0.78), 1, 1);
        }
    }

    private FormattedText Text(string text, Brush brush) => new(
        text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NameTypeface, NameSize, brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
