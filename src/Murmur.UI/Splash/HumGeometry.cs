using System.Windows;

namespace Murmur.UI.Splash;

/// <summary>
/// Where the splash's line is. At rest it is the icon's own line (murmur-icon.svg) with the tile 96 DIPs wide and
/// its flat ends extended to the splash's edges; before that it is a speech waveform that folds into it. The web
/// app's humLine.ts draws the same path for the About dialog's header.
/// </summary>
internal static class HumGeometry
{
    internal const double Width = 460;
    internal const double Height = 280;
    internal const double TileX = 182;
    internal const double TileY = 46;
    internal const double TileSize = 96;

    /// <summary>The icon's tile is 56 units wide on a 64-unit grid, with its corner at (4, 4).</summary>
    private const double TileUnits = 56;
    private const double TileOrigin = 4;
    private const double BaselineUnits = 43;

    /// <summary>The waveform's peak, in DIPs, and how far from each edge it is held down to nothing.</summary>
    private const double WavePeak = 21;
    private const double EdgeTaper = 70;

    internal static readonly double Scale = TileSize / TileUnits;
    private static readonly double OffsetX = TileX - TileOrigin * Scale;
    private static readonly double OffsetY = TileY - TileOrigin * Scale;

    /// <summary>Where the line's flat ends run.</summary>
    internal static readonly double Baseline = OffsetY + BaselineUnits * Scale;

    /// <summary>The line's stroke width and the tile's corner radius, in DIPs (5.8 and 15 units on the icon's grid).</summary>
    internal static readonly double StrokeWidth = 5.8 * Scale;
    internal static readonly double CornerRadius = 15 * Scale;

    /// <summary>
    /// The line at rest: <paramref name="count"/> points spaced evenly along it, from the splash's left edge to its
    /// right edge, rising into the m where the tile is.
    /// </summary>
    internal static Point[] RestPoints(int count)
    {
        var parts = Segments(-OffsetX / Scale, (Width - OffsetX) / Scale);
        var total = parts.Sum(part => part.Length);

        var points = new Point[count];
        var index = 0;
        var before = 0.0;
        for (var i = 0; i < count; i++)
        {
            var distance = total * i / (count - 1);
            while (index < parts.Length - 1 && distance > before + parts[index].Length)
            {
                before += parts[index].Length;
                index++;
            }

            var part = parts[index];
            var p = part.At(part.Length > 0 ? Math.Min(1, (distance - before) / part.Length) : 0);
            points[i] = new Point(OffsetX + p.X * Scale, OffsetY + p.Y * Scale);
        }

        return points;
    }

    /// <summary>
    /// The line at <paramref name="time"/> seconds: a waveform across the splash that settles into
    /// <paramref name="rest"/>, point by point. Written into <paramref name="into"/>, which has the same length.
    /// </summary>
    internal static void PointsAt(double time, Point[] rest, Point[] into)
    {
        var morph = SplashTimeline.Morph(time);
        var amplitude = SplashTimeline.WaveAmplitude(time);
        for (var i = 0; i < rest.Length; i++)
        {
            var x = Width * i / (rest.Length - 1);
            var y = Baseline + Speech(x, time) * amplitude;
            into[i] = new Point(x + (rest[i].X - x) * morph, y + (rest[i].Y - y) * morph);
        }
    }

    /// <summary>A speech-like wave: two tones under a slow envelope that travels, held to nothing at both edges.</summary>
    private static double Speech(double x, double time)
    {
        var edge = Math.Min(1, Math.Min(x / EdgeTaper, (Width - x) / EdgeTaper));
        var burst = 0.3 + 0.7 * Math.Abs(Math.Sin(x * 0.018 - time * 2.6));
        return WavePeak * edge * burst * (0.6 * Math.Sin(x * 0.085 - time * 10) + 0.4 * Math.Sin(x * 0.21 + time * 14));
    }

    /// <summary>
    /// The icon's line in icon units, from x0 to x1 along the baseline with the m in between:
    /// M0 43 H15 a4 4 0 0 0 4 -4 V28.5 a6.5 6.5 0 0 1 13 0 V43 V28.5 a6.5 6.5 0 0 1 13 0 V39 a4 4 0 0 0 4 4 H64.
    /// </summary>
    private static Segment[] Segments(double x0, double x1)
    {
        const double half = Math.PI / 2;
        return
        [
            Segment.Line(x0, BaselineUnits, 15, BaselineUnits),
            Segment.Arc(15, 39, 4, half, -half),
            Segment.Line(19, 39, 19, 28.5),
            Segment.Arc(25.5, 28.5, 6.5, Math.PI, Math.PI),
            Segment.Line(32, 28.5, 32, BaselineUnits),
            Segment.Line(32, BaselineUnits, 32, 28.5),
            Segment.Arc(38.5, 28.5, 6.5, Math.PI, Math.PI),
            Segment.Line(45, 28.5, 45, 39),
            Segment.Arc(49, 39, 4, Math.PI, -half),
            Segment.Line(49, BaselineUnits, x1, BaselineUnits),
        ];
    }

    /// <summary>A straight piece or an arc of the line. Angles in radians with y pointing down: 0 is right, PI/2 is down.</summary>
    private readonly record struct Segment(bool IsArc, Point A, Point B, double Radius, double Start, double Sweep, double Length)
    {
        internal static Segment Line(double x1, double y1, double x2, double y2) =>
            new(false, new Point(x1, y1), new Point(x2, y2), 0, 0, 0, Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1)));

        internal static Segment Arc(double centreX, double centreY, double radius, double start, double sweep) =>
            new(true, new Point(centreX, centreY), default, radius, start, sweep, Math.Abs(sweep) * radius);

        internal Point At(double t)
        {
            if (!IsArc)
                return new Point(A.X + (B.X - A.X) * t, A.Y + (B.Y - A.Y) * t);

            var angle = Start + Sweep * t;
            return new Point(A.X + Radius * Math.Cos(angle), A.Y + Radius * Math.Sin(angle));
        }
    }
}
