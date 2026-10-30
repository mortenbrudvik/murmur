namespace Murmur.Core.Insertion;

public enum InsertOutcome
{
    /// <summary>Ctrl+V was sent to the target window.</summary>
    Pasted,

    /// <summary>The text could not be pasted, so it was left on the clipboard for the owner to paste by hand.</summary>
    LeftOnClipboard,
}

/// <param name="TargetApp">Display name of the app that had focus.</param>
public sealed record InsertResult(InsertOutcome Outcome, string TargetApp);

/// <summary>Puts text into the focused window.</summary>
public interface ITextInserter
{
    /// <summary>
    /// Pastes <paramref name="text"/> into the focused window. Completes as soon as Ctrl+V has been sent (or the text
    /// was left on the clipboard); the owner's clipboard is put back in the background shortly after. Concurrent
    /// calls run one at a time, each waiting for the previous one's restore.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the paste was sent.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The clipboard could not be read or written; nothing was pasted.</exception>
    Task<InsertResult> InsertAsync(string text, CancellationToken ct);

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard (kept out of clipboard history) for the owner to paste by hand,
    /// after any pending restore. Blocks while one is pending, so call it off the UI thread.
    /// </summary>
    /// <returns>False when the clipboard could not be written; never throws.</returns>
    bool TryCopy(string text);
}
