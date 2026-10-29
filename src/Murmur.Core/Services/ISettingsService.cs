using Murmur.Core.Models;

namespace Murmur.Core.Services;

public interface ISettingsService
{
    MurmurSettings Load();
    void Save(MurmurSettings settings);
}
