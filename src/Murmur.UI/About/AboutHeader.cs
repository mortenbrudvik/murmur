using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Murmur.UI.Splash;

namespace Murmur.UI.About;

/// <summary>
/// The About window's header: the icon's tile in the middle, its line running the window's width. Outside the tile
/// the line hums, a slow wave a few pixels high, for as long as the header is on screen. With Windows' animations
/// turned off it is still.
/// </summary>
internal sealed class AboutHeader : FrameworkElement
{
    /// <summary>The header's drawing: the tile 64 DIPs wide, centred.</summary>
    internal static readonly HumFrame Frame = new(460, 198, 20, 64);

    private const double HeaderHeight = 104;
    private const int PointCount = 260;

    private static readonly HumTile Tile = new(Frame);

    private readonly Point[] _rest = HumGeometry.RestPoints(Frame, PointCount);
    private readonly Point[] _points = new Point[PointCount];
    private readonly Stopwatch _clock = new();
    private bool _humming;

    public AboutHeader()
    {
        Width = Frame.Width;
        Height = HeaderHeight;
        IsHitTestVisible = false;
        // The glow is wider than the header is tall.
        ClipToBounds = true;
        IsVisibleChanged += (_, _) => Hum(IsVisible && SystemParameters.ClientAreaAnimation);
    }

    /// <summary>
    /// Starts or stops redrawing with every frame. Stopped whenever the header is not on screen: the rendering event
    /// is the application's, and would keep a closed window's header alive and drawing.
    /// </summary>
    private void Hum(bool on)
    {
        if (on == _humming)
            return;

        _humming = on;
        if (on)
        {
            _clock.Start();
            CompositionTarget.Rendering += OnFrame;
        }
        else
        {
            CompositionTarget.Rendering -= OnFrame;
            _clock.Stop();
        }
    }

    private void OnFrame(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        if (_humming)
            HumGeometry.Hum(_clock.Elapsed.TotalSeconds, Frame, _rest, _points);
        else
            _rest.CopyTo(_points, 0);

        Tile.Draw(dc, HumPaint.Line(_points));
    }
}
