using Murmur.Core.Models;

namespace Murmur.Core.Services;

public sealed class SettingsService(MurmurPaths paths) : ISettingsService
{
    private readonly JsonFileStore<MurmurSettings> _store = new(paths.SettingsFile, MurmurSettings.Default);

    public MurmurSettings Load()
    {
        var settings = _store.Load();

        // A hand-edited or stale hotkey must never leave the app without a usable trigger.
        return settings.Hotkey is { IsValid: true }
            ? settings
            : settings with { Hotkey = HotkeyDefinition.Default };
    }

    public void Save(MurmurSettings settings) => _store.Save(settings);
}
