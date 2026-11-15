using Murmur.Core.Models;
using Murmur.Core.Services;
using NSubstitute;

namespace Murmur.Core.Tests.Helpers;

internal static class HistoryStubs
{
    /// <summary>
    /// Makes <paramref name="history"/>'s Add answer as the real store does, with a new entry stamped by
    /// <paramref name="time"/>, and keeps every entry it made in <paramref name="added"/> (locked, as dictations deliver
    /// off the test's thread).
    /// </summary>
    public static void RecordAdds(this IHistoryStore history, TimeProvider time, List<HistoryEntry> added) =>
        history.Add(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(call =>
        {
            var entry = new HistoryEntry(
                Guid.NewGuid(), time.GetUtcNow(), call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<bool>(2));
            lock (added)
                added.Add(entry);
            return entry;
        });
}
