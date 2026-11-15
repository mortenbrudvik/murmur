using System.Windows;
using System.Windows.Media;

namespace Murmur.UI.Splash;

/// <summary>
/// The icon's tile and the line through it, drawn the same way on the splash and in the About window's header: a glow
/// behind the tile, the line dimmed outside it, the tile, and the line white inside it.
/// </summary>
internal sealed class HumTile
{
    /// <summary>How much of the line outside the tile is left once the tile is lit.</summary>
    private const double DimmedLine = 0.38;

    /// <summary>How far the glow reaches from the tile's centre, in tile widths.</summary>
    private const double GlowRadius = 1.35;

    private readonly HumFrame _frame;
    private readonly Rect _tile;
    private readonly Point _centre;
    private readonly Geometry _clip;
    private readonly Pen _linePen;
    private readonly Pen _litPen;

    internal HumTile(HumFrame frame)
    {
        _frame = frame;
        _tile = new Rect(frame.TileX, frame.TileY, frame.TileSize, frame.TileSize);
        _centre = new Point(_tile.X + _tile.Width / 2, _tile.Y + _tile.Height / 2);
        _clip = HumPaint.Frozen(new RectangleGeometry(_tile, frame.CornerRadius, frame.CornerRadius));
        _linePen = HumPaint.LinePen(frame);
        _litPen = HumPaint.LitPen(frame);
    }

    /// <summary>
    /// Draws <paramref name="line"/> with the tile <paramref name="reveal"/> lit, from 0 (only the line, at full
    /// strength) to 1 (the tile and its glow in full, the line outside it dimmed). The tile grows the last tenth of its
    /// size as it lights up.
    /// </summary>
    internal void Draw(DrawingContext dc, Geometry line, double reveal = 1)
    {
        if (reveal > 0)
        {
            dc.PushOpacity(reveal);
            var glow = _frame.TileSize * GlowRadius;
            dc.DrawEllipse(HumPaint.GlowBrush, null, _centre, glow, glow);
            dc.Pop();
        }

        dc.PushOpacity(1 - (1 - DimmedLine) * reveal);
        dc.DrawGeometry(null, _linePen, line);
        dc.Pop();

        if (reveal <= 0)
            return;

        dc.PushOpacity(reveal);

        var scale = 0.9 + 0.1 * reveal;
        dc.PushTransform(new ScaleTransform(scale, scale, _centre.X, _centre.Y));
        dc.DrawRoundedRectangle(HumPaint.TileBrush, null, _tile, _frame.CornerRadius, _frame.CornerRadius);
        dc.Pop();

        dc.PushClip(_clip);
        dc.DrawGeometry(null, _litPen, line);
        dc.Pop();

        dc.Pop();
    }
}
