namespace Murmur.UI.Splash;

/// <summary>
/// Where the icon sits in a drawing of the line: how wide the drawing is, and the tile's corner and size in it. The
/// rest follows from the tile, because the line is the icon's own (murmur-icon.svg) scaled with it.
/// </summary>
internal readonly record struct HumFrame(double Width, double TileX, double TileY, double TileSize)
{
    /// <summary>The icon's tile is 56 units wide on a 64-unit grid, with its corner at (4, 4), and its line runs at 43.</summary>
    internal const double TileUnits = 56;
    internal const double TileOrigin = 4;
    internal const double BaselineUnits = 43;

    /// <summary>DIPs per unit of the icon's grid.</summary>
    internal double Scale => TileSize / TileUnits;

    /// <summary>Where the icon's grid has its origin in the drawing.</summary>
    internal double OffsetX => TileX - TileOrigin * Scale;
    internal double OffsetY => TileY - TileOrigin * Scale;

    /// <summary>Where the line's flat ends run.</summary>
    internal double Baseline => OffsetY + BaselineUnits * Scale;

    /// <summary>The line's stroke width and the tile's corner radius, in DIPs (5.8 and 15 units on the icon's grid).</summary>
    internal double StrokeWidth => 5.8 * Scale;
    internal double CornerRadius => 15 * Scale;
}
