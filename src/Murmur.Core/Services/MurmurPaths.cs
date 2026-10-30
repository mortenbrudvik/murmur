namespace Murmur.Core.Services;

/// <summary>Locations of everything Murmur persists, relative to one data root.</summary>
public sealed class MurmurPaths(string root)
{
    public static MurmurPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur"));

    public string SettingsFile { get; } = Path.Combine(root, "settings.json");
    public string VocabularyFile { get; } = Path.Combine(root, "vocabulary.json");
    public string HistoryFile { get; } = Path.Combine(root, "history.jsonl");
    public string ModelsDirectory { get; } = Path.Combine(root, "models");
    public string ModelVerificationFile { get; } = Path.Combine(root, "models", "verified.json");
    public string AudioDirectory { get; } = Path.Combine(root, "audio");
    public string LogsDirectory { get; } = Path.Combine(root, "Logs");

    /// <summary>Debug builds only: while this file exists, GPU detection reports no GPU at all.</summary>
    public string SimulateNoGpuFile { get; } = Path.Combine(root, "simulate-no-gpu");

    /// <summary>Debug builds only: while this file exists, the search for the CUDA 13 runtime finds nothing.</summary>
    public string SimulateNoCudaRuntimeFile { get; } = Path.Combine(root, "simulate-no-cuda-runtime");
}
