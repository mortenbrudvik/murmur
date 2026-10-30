using Murmur.Core.Models;

namespace Murmur.UI.Tray;

/// <summary>What a Windows notification says.</summary>
internal sealed record TrayNotice(string Title, string Message);

/// <summary>
/// Decides which engine status changes are worth a Windows notification while Murmur sits in the tray: the model is
/// ready after a download, the engine failed, or the download cannot reach the internet. It keeps its state for the
/// whole process and is meant for the UI thread alone.
/// <para>
/// A notification is for something the user cannot see: none is made while the main window is the active window, and
/// one that was skipped for that reason is not used up. Ready is announced only after a download, so an ordinary
/// sign-in start, which only loads a model that is already there, stays silent; and never with an error, which is a
/// model switch that failed.
/// </para>
/// </summary>
internal sealed class TrayNotifier
{
    /// <summary>This many failed attempts of one download in a row mean the connection is the problem.</summary>
    private const int OfflineRetries = 4;

    private bool _downloaded;
    private bool _errorNotified;
    private bool _offlineNotified;

    /// <summary>The notification to show for <paramref name="status"/>, or null for none.</summary>
    /// <param name="windowActive">Whether the main window is the active window.</param>
    /// <param name="hotkeyName">Names the hotkey in the ready notice; it is only used then.</param>
    public TrayNotice? OnStatus(EngineStatus status, bool windowActive, string hotkeyName)
    {
        // A download in progress, also the one of a model switch that runs while the current model is Ready.
        var downloading = status.State == EngineState.Downloading || status.Download is not null;
        if (downloading)
            _downloaded = true;

        if (status.State == EngineState.Ready && !downloading)
        {
            // Used up by this Ready, whether or not it was announced. A Ready with an error is a switch that failed and
            // stayed on the previous model: the download led to nothing, so there is nothing to announce.
            var announce = _downloaded && !windowActive && string.IsNullOrEmpty(status.Error);
            _downloaded = false;
            return announce ? new TrayNotice("Murmur is ready", $"Hold {hotkeyName} to talk.") : null;
        }

        if (status.State == EngineState.Error && !windowActive && !_errorNotified)
        {
            _errorNotified = true;
            return new TrayNotice("Murmur needs your attention", status.Error ?? "The speech engine failed.");
        }

        if (status.Download is { Retries: >= OfflineRetries } && !windowActive && !_offlineNotified)
        {
            _offlineNotified = true;
            return new TrayNotice(
                "Murmur can't download its speech model",
                "Check your internet connection. Murmur keeps trying.");
        }

        return null;
    }
}
