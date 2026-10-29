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
    /// <summary>Pastes <paramref name="text"/> into the focused window. Concurrent calls run one at a time.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the paste was sent.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The clipboard could not be read or written; nothing was pasted.</exception>
    Task<InsertResult> InsertAsync(string text, CancellationToken ct);
}
