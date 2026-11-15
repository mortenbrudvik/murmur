using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shouldly;

namespace Murmur.UI.Tests.Themes;

/// <summary>
/// The windows' colour dictionaries build their shared brushes from Palette.xaml. These load each one as the app does and
/// check that the palette reached it: a broken reference only fails when the app starts.
/// </summary>
public sealed class ColorDictionaryTests
{
    [Theory]
    [InlineData("PillColors", "PillAccentBrush", "PaletteAccentColor")]
    [InlineData("PillColors", "PillTextBrush", "PaletteTextColor")]
    [InlineData("AboutColors", "AboutTextBrush", "PaletteTextColor")]
    [InlineData("AboutColors", "AboutStrongTextBrush", "PaletteStrongTextColor")]
    [InlineData("AboutColors", "AboutMutedBrush", "PaletteMutedColor")]
    [InlineData("AboutColors", "AboutDimBrush", "PaletteDimColor")]
    [InlineData("AboutColors", "AboutAccentBrush", "PaletteAccentColor")]
    [InlineData("AboutColors", "AboutErrorBrush", "PaletteErrorColor")]
    [InlineData("AboutColors", "AboutBorderBrush", "PaletteBorderColor")]
    [InlineData("SplashColors", "SplashStrongTextBrush", "PaletteStrongTextColor")]
    [InlineData("SplashColors", "SplashMutedBrush", "PaletteMutedColor")]
    [InlineData("SplashColors", "SplashDimBrush", "PaletteDimColor")]
    [InlineData("SplashColors", "SplashAccentBrush", "PaletteAccentColor")]
    [InlineData("SplashColors", "SplashOkBrush", "PaletteOkColor")]
    [InlineData("SplashColors", "SplashErrorBrush", "PaletteErrorColor")]
    [InlineData("SplashColors", "SplashBorderBrush", "PaletteBorderColor")]
    public void Brush_TakesItsColourFromThePalette(string dictionary, string brushKey, string colorKey)
    {
        StaHost.Run(() =>
        {
            var colors = Load(dictionary);
            var palette = Load("Palette");

            colors[brushKey].ShouldBeOfType<SolidColorBrush>().Color.ShouldBe((Color)palette[colorKey]);
        });
    }

    [Fact]
    public void PulsingDots_LoadOnTheirOwn()
    {
        StaHost.Run(() => Load("PulsingDots")["PulsingDotsTemplate"].ShouldBeOfType<ControlTemplate>());
    }

    private static ResourceDictionary Load(string name) =>
        (ResourceDictionary)Application.LoadComponent(new Uri($"/Murmur.UI;component/Themes/{name}.xaml", UriKind.Relative));
}
