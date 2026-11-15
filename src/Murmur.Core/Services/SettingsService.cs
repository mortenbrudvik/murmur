using Murmur.Core.Input;
using Murmur.Core.Models;

namespace Murmur.Core.Services;

/// <summary>
/// The settings file, read once and then kept in memory: every dictation reads the settings, and the file only changes
/// through this service (one Murmur runs at a time). A failed read is not kept, so the next call tries the file again. An
/// edit made to the file by hand while Murmur runs is not seen until Murmur restarts, and the next save overwrites it.
/// </summary>
/// <param name="layout">Renames a saved symbol-key hotkey after the current layout when the settings are loaded.</param>
public sealed class SettingsService(MurmurPaths paths, IKeyboardLayout layout) : ISettingsService
{
    private readonly JsonFileStore<MurmurSettings> _store = new(paths.SettingsFile, MurmurSettings.Default);

    // Guards _stored, and makes an update's load and save one step.
    private readonly Lock _lock = new();

    // The settings as stored (before Normalize), or null until the file has been read.
    private MurmurSettings? _stored;

    public MurmurSettings Load()
    {
        lock (_lock)
            return Normalize(Stored());
    }

    public MurmurSettings Update(Func<MurmurSettings, MurmurSettings> change)
    {
        lock (_lock)
        {
            var updated = change(Normalize(Stored()));
            _store.Save(updated);
            _stored = updated;
            return updated;
        }
    }

    /// <summary>The stored settings, read from the file on first use. Call inside the lock.</summary>
    private MurmurSettings Stored() => _stored ??= _store.Load();

    /// <summary>
    /// Repairs the hotkey and names it after the current keyboard layout. Done on every load, not once: the layout can
    /// change while Murmur runs.
    /// </summary>
    private MurmurSettings Normalize(MurmurSettings settings)
    {
        // A hand-edited or stale hotkey must never leave the app without a usable trigger.
        if (settings.Hotkey is not { IsValid: true } hotkey)
            return settings with { Hotkey = HotkeyDefinition.Default };

        // The saved name was made on the layout in use when the key was captured, which may have changed since (or the
        // name predates layout-aware names). The key code is what counts; the name is only for show.
        return KeyNames.IsLayoutDependent(hotkey.VirtualKey)
            ? settings with { Hotkey = hotkey with { DisplayName = KeyNames.Describe(hotkey.VirtualKey, hotkey.Modifiers, layout) } }
            : settings;
    }
}
