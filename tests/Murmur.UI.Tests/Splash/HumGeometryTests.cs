using System.Windows;
using Murmur.UI.Splash;
using Shouldly;

namespace Murmur.UI.Tests.Splash;

/// <summary>
/// The splash's line: the icon's own path (murmur-icon.svg), scaled so the tile is 96 DIPs wide with its corner at
/// (182, 46), and extended to the splash's edges. The About window's header draws the same path in a smaller frame.
/// </summary>
public sealed class HumGeometryTests
{
    private const double Scale = 96.0 / 56;
    private static readonly double Baseline = 46 - 4 * Scale + 43 * Scale;

    [Fact]
    public void Frame_IsTheDesigns()
    {
        HumGeometry.Width.ShouldBe(460);
        HumGeometry.Height.ShouldBe(280);
        HumGeometry.TileX.ShouldBe(182);
        HumGeometry.TileY.ShouldBe(46);
        HumGeometry.TileSize.ShouldBe(96);
        HumGeometry.Baseline.ShouldBe(Baseline, 1e-9);
    }

    [Fact]
    public void RestPoints_RunFromEdgeToEdgeOnTheBaseline()
    {
        var points = HumGeometry.RestPoints(320);

        points.Length.ShouldBe(320);
        points[0].X.ShouldBe(0, 1e-6);
        points[0].Y.ShouldBe(Baseline, 1e-6);
        points[^1].X.ShouldBe(460, 1e-6);
        points[^1].Y.ShouldBe(Baseline, 1e-6);
    }

    [Fact]
    public void RestPoints_RiseIntoTheMInsideTheTileAndNowhereElse()
    {
        var raised = HumGeometry.RestPoints(640).Where(p => p.Y < Baseline - 0.5).ToArray();

        raised.ShouldNotBeEmpty();
        raised.ShouldAllBe(p => p.X > 182 && p.X < 278 && p.Y > 46);
    }

    [Fact]
    public void RestPoints_TopOutWhereTheIconsHumpsDo()
    {
        // The humps' tops are at y = 22 on the icon's grid, 21 units above its baseline at 43.
        HumGeometry.RestPoints(1200).Min(p => p.Y).ShouldBe(Baseline - 21 * Scale, 0.05);
    }

    [Fact]
    public void RestPoints_AreEvenlySpacedAlongTheLine()
    {
        var points = HumGeometry.RestPoints(400);
        var first = (points[1] - points[0]).Length;

        // The middle stem is walked down and back up, so neighbours can be closer there, never further apart.
        for (var i = 1; i < points.Length; i++)
            (points[i] - points[i - 1]).Length.ShouldBeLessThan(first * 1.01);
    }

    /// <summary>The About window's header: the same line with the tile 64 DIPs wide at (198, 20).</summary>
    private static readonly HumFrame Header = new(460, 198, 20, 64);

    [Fact]
    public void Frame_Splash_IsTheSplashs()
    {
        HumGeometry.Splash.ShouldBe(new HumFrame(460, 182, 46, 96));
    }

    [Fact]
    public void Frame_DerivesItsMeasuresFromTheTile()
    {
        const double scale = 64.0 / 56;

        Header.Scale.ShouldBe(scale, 1e-9);
        Header.Baseline.ShouldBe(20 - 4 * scale + 43 * scale, 1e-9);
        Header.StrokeWidth.ShouldBe(5.8 * scale, 1e-9);
        Header.CornerRadius.ShouldBe(15 * scale, 1e-9);
    }

    [Fact]
    public void RestPoints_ForAnotherFrame_PutTheLineInThatFrame()
    {
        var points = HumGeometry.RestPoints(Header, 640);

        points[0].X.ShouldBe(0, 1e-6);
        points[0].Y.ShouldBe(Header.Baseline, 1e-6);
        points[^1].X.ShouldBe(460, 1e-6);
        points[^1].Y.ShouldBe(Header.Baseline, 1e-6);
        var raised = points.Where(p => p.Y < Header.Baseline - 0.5).ToArray();
        raised.ShouldNotBeEmpty();
        raised.ShouldAllBe(p => p.X > 198 && p.X < 262 && p.Y > 20);
    }

    [Fact]
    public void Hum_LeavesTheIconInsideTheTileStill_AndEveryPointWhereItIsAcross()
    {
        var rest = HumGeometry.RestPoints(Header, 260);
        var points = new Point[260];

        HumGeometry.Hum(1.3, Header, rest, points);

        for (var i = 0; i < rest.Length; i++)
        {
            points[i].X.ShouldBe(rest[i].X);
            if (rest[i].X >= 198 && rest[i].X <= 262)
                points[i].Y.ShouldBe(rest[i].Y);
        }
    }

    [Fact]
    public void Hum_MovesTheLineOutsideTheTileByAFewPixelsAtMost()
    {
        var rest = HumGeometry.RestPoints(Header, 260);
        var points = new Point[260];

        HumGeometry.Hum(1.3, Header, rest, points);

        var moved = Enumerable.Range(0, rest.Length).Max(i => Math.Abs(points[i].Y - rest[i].Y));
        moved.ShouldBeInRange(1, 3.9001);
    }

    [Fact]
    public void Hum_MovesWithTime()
    {
        var rest = HumGeometry.RestPoints(Header, 260);
        var earlier = new Point[260];
        var later = new Point[260];

        HumGeometry.Hum(1.0, Header, rest, earlier);
        HumGeometry.Hum(1.4, Header, rest, later);

        Enumerable.Range(0, rest.Length).Max(i => Math.Abs(later[i].Y - earlier[i].Y)).ShouldBeGreaterThan(0.5);
    }

    [Fact]
    public void PointsAt_TheStart_IsAFlatLineAcrossTheSplash()
    {
        var rest = HumGeometry.RestPoints(320);
        var points = new Point[320];

        HumGeometry.PointsAt(0, rest, points);

        points[0].X.ShouldBe(0, 1e-6);
        points[^1].X.ShouldBe(460, 1e-6);
        points.ShouldAllBe(p => Math.Abs(p.Y - Baseline) < 1e-6);
    }

    [Fact]
    public void PointsAt_WhileSpeaking_MovesButStaysPinnedAtBothEdgesAndInsideTheSplash()
    {
        var rest = HumGeometry.RestPoints(320);
        var points = new Point[320];

        HumGeometry.PointsAt(0.5, rest, points);

        points[0].Y.ShouldBe(Baseline, 1e-6);
        points[^1].Y.ShouldBe(Baseline, 1e-6);
        points.Max(p => Math.Abs(p.Y - Baseline)).ShouldBeInRange(4, 21.0001);
    }

    [Fact]
    public void PointsAt_TheEnd_IsTheIconsLine()
    {
        var rest = HumGeometry.RestPoints(320);
        var points = new Point[320];

        HumGeometry.PointsAt(SplashTimeline.End, rest, points);

        for (var i = 0; i < rest.Length; i++)
            (points[i] - rest[i]).Length.ShouldBeLessThan(0.01);
    }
}
