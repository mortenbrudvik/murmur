using System.Windows;
using System.Windows.Media;

namespace Murmur.UI.Themes;

/// <summary>
/// Gives the recording pill the system colours while a Windows contrast theme is on. The pill's own colours
/// (Themes/PillColors.xaml) are merged into the app resources; this sets the same keys on the app resources themselves,
/// which win over merged dictionaries, and removes them again when the contrast theme is turned off. The pill refers to
/// them with DynamicResource, so it follows at once.
/// </summary>
internal static class PillContrast
{
    internal static readonly string[] BrushKeys =
    [
        "PillBackgroundBrush", "PillBorderBrush", "PillRecordingBrush", "PillRecordingHaloBrush", "PillAccentBrush",
        "PillTextBrush",
    ];

    internal const string ShadowColorKey = "PillShadowColor";

    /// <summary>Applies the contrast colours to <paramref name="resources"/>, or takes them away.</summary>
    public static void Apply(ResourceDictionary resources, bool highContrast)
    {
        if (!highContrast)
        {
            foreach (var key in BrushKeys)
                resources.Remove(key);
            resources.Remove(ShadowColorKey);
            return;
        }

        resources["PillBackgroundBrush"] = SystemColors.WindowBrush;
        resources["PillBorderBrush"] = SystemColors.WindowTextBrush;
        resources["PillTextBrush"] = SystemColors.WindowTextBrush;
        resources["PillAccentBrush"] = SystemColors.HighlightBrush;
        resources["PillRecordingBrush"] = SystemColors.HighlightBrush;
        resources["PillRecordingHaloBrush"] = Brushes.Transparent;
        resources[ShadowColorKey] = Colors.Transparent;
    }

    /// <summary>Applies the current contrast setting to <paramref name="resources"/> and follows it from then on. UI thread.</summary>
    public static void Follow(ResourceDictionary resources)
    {
        Apply(resources, SystemParameters.HighContrast);
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
                Apply(resources, SystemParameters.HighContrast);
        };
    }
}
