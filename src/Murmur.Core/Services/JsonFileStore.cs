using System.Text.Json;
using Murmur.Common;
using Serilog;

namespace Murmur.Core.Services;

/// <summary>
/// A single JSON document on disk. Reads fall back to <c>defaults</c> when the file is missing or corrupt;
/// writes go to a temp file first and are renamed into place so a crash cannot leave a half-written file.
/// </summary>
public sealed class JsonFileStore<T>(string path, T defaults) where T : class
{
    private readonly Lock _lock = new();
    private readonly ILogger _logger = Log.ForContext<JsonFileStore<T>>();

    public T Load()
    {
        lock (_lock)
        {
            if (!File.Exists(path))
                return defaults;

            try
            {
                var json = File.ReadAllText(path);
                return JsonUtils.Deserialize(json, typeof(T)) as T ?? defaults;
            }
            catch (JsonException ex)
            {
                _logger.Warning(ex, "Corrupt JSON file {Path}, using defaults", path);
                return defaults;
            }
        }
    }

    public void Save(T value)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonUtils.Serialize(value);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
    }
}
