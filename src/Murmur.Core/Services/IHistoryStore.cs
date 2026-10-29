using Murmur.Core.Models;

namespace Murmur.Core.Services;

/// <summary>Every dictation, kept so that nothing is ever lost, even when pasting failed.</summary>
public interface IHistoryStore
{
    /// <summary>Records a dictation, stamping it with a new id and the current UTC time.</summary>
    HistoryEntry Add(string text, string targetApp, bool pasted);

    /// <summary>
    /// Newest entries first, at most <paramref name="limit"/> of them. A non-blank <paramref name="search"/>
    /// keeps only entries whose text contains it, ignoring case.
    /// </summary>
    IReadOnlyList<HistoryEntry> Query(string? search, int limit);

    /// <returns><c>true</c> if an entry with that id existed and was removed.</returns>
    bool Delete(Guid id);

    void Clear();
}
