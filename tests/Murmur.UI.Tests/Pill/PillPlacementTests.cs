using System.Windows;
using Murmur.UI.Pill;
using Shouldly;

namespace Murmur.UI.Tests.Pill;

public class PillPlacementTests
{
    [Fact]
    public void Compute_PrimaryMonitor_CentersAboveTheBottomMargin()
    {
        var position = PillPlacement.Compute(new Rect(0, 0, 1920, 1040), new Size(120, 30));

        position.ShouldBe(new Point(900, 998));
    }

    [Fact]
    public void Compute_SecondMonitor_IsRelativeToThatWorkArea()
    {
        var position = PillPlacement.Compute(new Rect(1920, 0, 2560, 1400), new Size(120, 30));

        position.ShouldBe(new Point(3140, 1358));
    }

    [Fact]
    public void Compute_MonitorLeftOfThePrimary_HandlesNegativeCoordinates()
    {
        var position = PillPlacement.Compute(new Rect(-1920, 0, 1920, 1080), new Size(100, 30));

        position.ShouldBe(new Point(-1010, 1038));
    }

    [Fact]
    public void Compute_CustomBottomMargin_IsHonoured()
    {
        var position = PillPlacement.Compute(new Rect(0, 0, 1000, 600), new Size(100, 40), bottomMarginDip: 20);

        position.ShouldBe(new Point(450, 540));
    }
}
