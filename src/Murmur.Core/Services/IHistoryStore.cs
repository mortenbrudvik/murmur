using Murmur.Core.Models;

namespace Murmur.Core.Services;

/// <summary>Every dictation, kept so that nothing is ever lost, even when pasting failed.</summary>
public interface IHistoryStore
{
    /// <summary>Records a dictation, stamping it with a new id and the current UTC time.</summary>
    HistoryEntry Add(string text, string targetApp, bool pasted);

    /// <summary>
    /// A page of entries, newest first, at most <paramref name="limit"/> of them. A non-blank <paramref name="search"/>
    /// keeps only entries whose text contains it, ignoring case. With <paramref name="before"/>, the page holds only
    /// entries older than that one by their position in the store, not by timestamp, so a page boundary never repeats
    /// or skips an entry however many were added since the previous page, or share a timestamp. The search applies to
    /// the older entries only; the entry <paramref name="before"/> names need not match it. An id the store does not
    /// hold (the entry was deleted, or the history cleared) gives an empty page with no more behind it.
    /// </summary>
    HistoryPageResult Query(string? search, int limit, Guid? before);

    /// <returns><c>true</c> if an entry with that id existed and was removed.</returns>
    bool Delete(Guid id);

    void Clear();
}
