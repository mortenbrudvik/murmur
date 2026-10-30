namespace Murmur.Core.Input;

/// <summary>
/// Whether Windows let Murmur install its keyboard hook, which is what the hotkey needs. It starts out installed: the
/// start-up writes it once, from the UI thread, if the hook could not be installed even on the second attempt, and the
/// tray and the web app only read it. Sent to the web app as <c>{ hookInstalled, error }</c>.
/// </summary>
public sealed class InputStatus
{
    /// <summary>False once the hook could not be installed; dictation is off for the rest of this run then.</summary>
    public bool HookInstalled { get; set; } = true;

    /// <summary>Why the hook could not be installed (the last error Windows or the hook thread gave), for the log and support.</summary>
    public string? Error { get; set; }
}
