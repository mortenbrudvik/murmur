using Murmur.Core.Models;

namespace Murmur.Core.Speech;

/// <summary>What the tray icon says about the engine: its tooltip, and whether it shows the ready icon.</summary>
public static class TrayStatusText
{
    /// <summary>The notification area's tooltip holds 128 characters, including the terminating null.</summary>
    public const int MaxToolTipLength = 127;

    /// <summary>
    /// The tooltip for an engine state; long error messages are cut to fit. Ready stays Ready while a model switch
    /// downloads the next model: the current one keeps dictating meanwhile, and the download shows in Settings. The
    /// load of the next model is not "loading model…" either, for the same reason. Without the keyboard hook, whenever
    /// the engine could dictate (Ready, or the load of a switch) the tooltip says the hotkey is not working instead.
    /// </summary>
    public static string ToolTip(EngineStatus status, string hotkeyName, bool hookInstalled = true)
    {
        var text = status.State switch
        {
            _ when !hookInstalled && ShowsReadyIcon(status) => "Murmur — the hotkey isn't working",
            _ when status.Switching => $"Murmur — switching to {status.ModelName}…",
            EngineState.Ready => $"Murmur — hold {hotkeyName} to talk",
            EngineState.Downloading when status.Download is { Reconnecting: true } => "Murmur — reconnecting to download the speech model…",
            EngineState.Downloading => $"Murmur — downloading speech model {EngineStatusText.Percent(status.Download)}%",
            EngineState.Error => $"Murmur — {status.Error ?? "the speech engine failed"}",
            _ => "Murmur — loading model…",
        };

        return text.Length <= MaxToolTipLength ? text : string.Concat(text.AsSpan(0, MaxToolTipLength - 1), "…");
    }

    /// <summary>
    /// The gradient dot: the engine can dictate, which includes the load of a switched-to model (its status is Loading,
    /// but the previous model is still there to dictate with).
    /// </summary>
    public static bool ShowsReadyIcon(EngineStatus status) => status.State == EngineState.Ready || status.Switching;
}
