using Murmur.Core.Models;

namespace Murmur.Core.Services;

public sealed class SettingsService(MurmurPaths paths) : ISettingsService
{
    private readonly JsonFileStore<MurmurSettings> _store = new(paths.SettingsFile, MurmurSettings.Default);
    private readonly Lock _updateLock = new();

    public MurmurSettings Load()
    {
        var settings = _store.Load();

        // A hand-edited or stale hotkey must never leave the app without a usable trigger.
        return settings.Hotkey is { IsValid: true }
            ? settings
            : settings with { Hotkey = HotkeyDefinition.Default };
    }

    public void Save(MurmurSettings settings)
    {
        // Shares Update's lock, so a plain save cannot slip in between an update's load and its save.
        lock (_updateLock)
            _store.Save(settings);
    }

    public MurmurSettings Update(Func<MurmurSettings, MurmurSettings> change)
    {
        lock (_updateLock)
        {
            var updated = change(Load());
            _store.Save(updated);
            return updated;
        }
    }
}
