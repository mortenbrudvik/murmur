using System.Windows;

namespace Murmur.UI.Splash;

/// <summary>
/// Where the line is. At rest it is the icon's own line (murmur-icon.svg), scaled with the tile and with its flat
/// ends extended to the drawing's edges. On the splash it starts as a speech waveform that folds into that line; in
/// the About window's header it hums a little outside the tile.
/// </summary>
internal static class HumGeometry
{
    internal const double Width = 460;
    internal const double Height = 280;
    internal const double TileX = 182;
    internal const double TileY = 46;
    internal const double TileSize = 96;

    /// <summary>The waveform's peak, in DIPs, and how far from each edge it is held down to nothing.</summary>
    private const double WavePeak = 21;
    private const double EdgeTaper = 70;

    /// <summary>How far from the tile the humming line is free to move; inside the tile it is the icon, and stays still.</summary>
    private const double HumFallOff = 46;

    /// <summary>The splash's drawing: the tile 96 DIPs wide.</summary>
    internal static readonly HumFrame Splash = new(Width, TileX, TileY, TileSize);

    /// <summary>Where the splash's line has its flat ends.</summary>
    internal static readonly double Baseline = Splash.Baseline;

    /// <summary>The splash's tile's corner radius, in DIPs.</summary>
    internal static readonly double CornerRadius = Splash.CornerRadius;

    /// <summary>The splash's line at rest.</summary>
    internal static Point[] RestPoints(int count) => RestPoints(Splash, count);

    /// <summary>
    /// The line at rest in <paramref name="frame"/>: <paramref name="count"/> points spaced evenly along it, from the
    /// drawing's left edge to its right edge, rising into the m where the tile is.
    /// </summary>
    internal static Point[] RestPoints(HumFrame frame, int count)
    {
        var scale = frame.Scale;
        var offsetX = frame.OffsetX;
        var offsetY = frame.OffsetY;
        var parts = Segments(-offsetX / scale, (frame.Width - offsetX) / scale);
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
            points[i] = new Point(offsetX + p.X * scale, offsetY + p.Y * scale);
        }

        return points;
    }

    /// <summary>
    /// The line humming at <paramref name="time"/> seconds: outside the tile a slow wave a few DIPs high, growing
    /// with the distance from the tile; inside it the line is the icon and does not move. Written into
    /// <paramref name="into"/>, which has the same length as <paramref name="rest"/>.
    /// </summary>
    internal static void Hum(double time, HumFrame frame, Point[] rest, Point[] into)
    {
        var tileRight = frame.TileX + frame.TileSize;
        for (var i = 0; i < rest.Length; i++)
        {
            var x = rest[i].X;
            var outside = x < frame.TileX ? frame.TileX - x : x > tileRight ? x - tileRight : 0;
            var fall = Math.Min(1, outside / HumFallOff);
            into[i] = new Point(x, rest[i].Y + fall * (2.6 * Math.Sin(x * 0.06 - time * 2.2) + 1.3 * Math.Sin(x * 0.13 + time * 3.1)));
        }
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
            Segment.Line(x0, HumFrame.BaselineUnits, 15, HumFrame.BaselineUnits),
            Segment.Arc(15, 39, 4, half, -half),
            Segment.Line(19, 39, 19, 28.5),
            Segment.Arc(25.5, 28.5, 6.5, Math.PI, Math.PI),
            Segment.Line(32, 28.5, 32, HumFrame.BaselineUnits),
            Segment.Line(32, HumFrame.BaselineUnits, 32, 28.5),
            Segment.Arc(38.5, 28.5, 6.5, Math.PI, Math.PI),
            Segment.Line(45, 28.5, 45, 39),
            Segment.Arc(49, 39, 4, Math.PI, -half),
            Segment.Line(49, HumFrame.BaselineUnits, x1, HumFrame.BaselineUnits),
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
