using Murmur.Core.Input;
using Murmur.Core.Models;

namespace Murmur.Core.Services;

/// <param name="layout">Renames a saved symbol-key hotkey after the current layout when the settings are loaded.</param>
public sealed class SettingsService(MurmurPaths paths, IKeyboardLayout layout) : ISettingsService
{
    private readonly JsonFileStore<MurmurSettings> _store = new(paths.SettingsFile, MurmurSettings.Default);
    private readonly Lock _updateLock = new();

    public MurmurSettings Load()
    {
        var settings = _store.Load();

        // A hand-edited or stale hotkey must never leave the app without a usable trigger.
        if (settings.Hotkey is not { IsValid: true } hotkey)
            return settings with { Hotkey = HotkeyDefinition.Default };

        // The saved name was made on the layout in use when the key was captured, which may have changed since (or the
        // name predates layout-aware names). The key code is what counts; the name is only for show.
        return KeyNames.IsLayoutDependent(hotkey.VirtualKey)
            ? settings with { Hotkey = hotkey with { DisplayName = KeyNames.Describe(hotkey.VirtualKey, hotkey.Modifiers, layout) } }
            : settings;
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
