using System.Windows;
using System.Windows.Media;
using Murmur.UI.Themes;
using Shouldly;

namespace Murmur.UI.Tests.Themes;

public class PillContrastTests
{
    [Fact]
    public void Apply_HighContrast_SetsEveryPillColourToASystemColour()
    {
        StaHost.Run(() =>
        {
            var resources = new ResourceDictionary();

            PillContrast.Apply(resources, highContrast: true);

            foreach (var key in PillContrast.BrushKeys)
                resources[key].ShouldBeAssignableTo<SolidColorBrush>();
            resources["PillBackgroundBrush"].ShouldBe(SystemColors.WindowBrush);
            resources["PillTextBrush"].ShouldBe(SystemColors.WindowTextBrush);
            resources["PillAccentBrush"].ShouldBe(SystemColors.HighlightBrush);
            resources[PillContrast.ShadowColorKey].ShouldBe(Colors.Transparent);
        });
    }

    [Fact]
    public void Apply_ContrastTurnedOff_FallsBackToTheMergedPillColours()
    {
        StaHost.Run(() =>
        {
            var pillColors = new ResourceDictionary();
            var ownBackground = new SolidColorBrush(Color.FromArgb(0xE0, 0x16, 0x18, 0x20));
            pillColors["PillBackgroundBrush"] = ownBackground;
            var resources = new ResourceDictionary { MergedDictionaries = { pillColors } };

            PillContrast.Apply(resources, highContrast: true);
            resources["PillBackgroundBrush"].ShouldBe(SystemColors.WindowBrush);

            PillContrast.Apply(resources, highContrast: false);
            resources["PillBackgroundBrush"].ShouldBeSameAs(ownBackground);
            resources.Contains(PillContrast.ShadowColorKey).ShouldBeFalse();
        });
    }
}
