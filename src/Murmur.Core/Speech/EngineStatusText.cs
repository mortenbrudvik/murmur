using Murmur.Core.Models;

namespace Murmur.Core.Speech;

/// <summary>What kind of news a status line is; the splash colours it and picks its marker by this.</summary>
public enum EngineStatusTone
{
    Progress,
    Info,
    Ready,
    Error
}

public sealed record EngineStatusLine(string Text, EngineStatusTone Tone);

/// <summary>
/// The engine's status as one line, for the splash. It is the twin of <c>describeEngineStatus</c> in the web app
/// (<c>src/WebApp/src/features/settings/engineStatusText.ts</c>), which words the banner and the Settings card: the
/// splash is on screen before the web app exists, so it cannot ask it. Change the wording in both, and in both tests.
/// </summary>
public static class EngineStatusText
{
    public static EngineStatusLine Describe(EngineStatus status) => status.State switch
    {
        EngineState.Downloading => new EngineStatusLine(
            status.Download?.Reconnecting == true ? "Reconnecting…" : $"Downloading speech model… {Percent(status.Download)}%",
            EngineStatusTone.Progress),
        EngineState.Ready => new EngineStatusLine(
            string.IsNullOrEmpty(status.Runtime) ? "Ready" : $"Ready · {RuntimeLabel(status.Runtime)}",
            EngineStatusTone.Ready),
        EngineState.Error => new EngineStatusLine(
            string.IsNullOrEmpty(status.Error) ? "The speech model could not be loaded." : status.Error,
            EngineStatusTone.Error),
        // Loading and NotStarted: from the user's side the model is on its way.
        _ => new EngineStatusLine(
            status.Switching ? $"Switching to {status.ModelName}…" : "Loading model…",
            EngineStatusTone.Info),
    };

    /// <summary>The runtime names the engine reports ("Cuda", "Vulkan", "Cpu") as they are written.</summary>
    private static string RuntimeLabel(string runtime) => runtime switch
    {
        "Cuda" => "CUDA",
        "Cpu" => "CPU",
        _ => runtime,
    };

    /// <summary>Whole bytes times 100, then divided, so 99.9 % reads 99 and never 100 before the last byte.</summary>
    private static long Percent(DownloadProgress? download)
    {
        if (download is null || download.TotalBytes <= 0)
            return 0;

        return Math.Clamp(download.BytesReceived, 0, download.TotalBytes) * 100 / download.TotalBytes;
    }
}
