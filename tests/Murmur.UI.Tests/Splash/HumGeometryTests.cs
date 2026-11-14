using System.Windows;
using Murmur.UI.Splash;
using Shouldly;

namespace Murmur.UI.Tests.Splash;

/// <summary>
/// The splash's line: the icon's own path (murmur-icon.svg), scaled so the tile is 96 DIPs wide with its corner at
/// (182, 46), and extended to the splash's edges. The web app's humLine.ts draws the same path for the About dialog.
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
