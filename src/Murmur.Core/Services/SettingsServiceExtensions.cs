using Serilog;

namespace Murmur.Core.Services;

public static class SettingsServiceExtensions
{
    /// <summary>
    /// The hotkey's display name, for the tray. When the settings cannot be read (the file is locked, or access to it is
    /// denied), logs a warning and returns <paramref name="fallback"/>: a name for show must not fail its caller.
    /// </summary>
    public static string ReadHotkeyName(this ISettingsService settings, string fallback)
    {
        try
        {
            return settings.Load().Hotkey.DisplayName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.ForContext(typeof(SettingsServiceExtensions))
                .Warning(ex, "Could not read the hotkey from the settings; showing {HotkeyName}", fallback);
            return fallback;
        }
    }
}
