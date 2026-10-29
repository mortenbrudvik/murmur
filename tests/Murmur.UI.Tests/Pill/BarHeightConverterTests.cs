using System.Globalization;
using Murmur.UI.Pill;
using Shouldly;

namespace Murmur.UI.Tests.Pill;

public class BarHeightConverterTests
{
    private readonly BarHeightConverter _converter = new();

    [Theory]
    [InlineData(0.2, 3.2)]
    [InlineData(0.6, 9.6)]
    [InlineData(1.0, 16.0)]
    public void Convert_Level_ScalesToSixteenDips(double level, double expectedHeight)
    {
        var height = _converter.Convert(level, typeof(double), null, CultureInfo.InvariantCulture);

        height.ShouldBeOfType<double>().ShouldBe(expectedHeight, 0.001);
    }

    [Fact]
    public void Convert_NotADouble_IsZero()
    {
        _converter.Convert("loud", typeof(double), null, CultureInfo.InvariantCulture).ShouldBe(0d);
    }
}
