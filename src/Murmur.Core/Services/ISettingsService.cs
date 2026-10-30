using Murmur.Core.Models;

namespace Murmur.Core.Services;

public interface ISettingsService
{
    MurmurSettings Load();
    void Save(MurmurSettings settings);

    /// <summary>
    /// Loads the settings, applies <paramref name="change"/> and saves the result as one step, so concurrent callers
    /// cannot overwrite each other's change. Returns the saved settings.
    /// </summary>
    MurmurSettings Update(Func<MurmurSettings, MurmurSettings> change);
}
