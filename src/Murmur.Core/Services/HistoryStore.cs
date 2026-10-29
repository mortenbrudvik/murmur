using System.Text.Json;
using Murmur.Common;
using Murmur.Core.Models;
using Serilog;

namespace Murmur.Core.Services;

/// <summary>
/// History as JSON Lines, one entry per line, oldest first. The file is read once on first use and kept in memory;
/// <see cref="Add"/> appends a single line, while <see cref="Delete"/> and <see cref="Clear"/> rewrite the file via a
/// temp file so a crash cannot leave it half-written.
/// </summary>
public sealed class HistoryStore(MurmurPaths paths, TimeProvider time) : IHistoryStore
{
    private readonly Lock _lock = new();
    private readonly ILogger _logger = Log.ForContext<HistoryStore>();

    private List<HistoryEntry>? _entries;
    private bool _fileEndsWithNewline = true;

    public HistoryEntry Add(string text, string targetApp, bool pasted)
    {
        lock (_lock)
        {
            var entries = EnsureLoaded();
            var entry = new HistoryEntry(Guid.NewGuid(), time.GetUtcNow(), text, targetApp, pasted);

            Directory.CreateDirectory(Path.GetDirectoryName(paths.HistoryFile)!);

            // A crash mid-append can leave a last line without its newline; start a fresh line so
            // the new entry does not get glued onto it.
            var line = (_fileEndsWithNewline ? "" : "\n") + JsonUtils.Serialize(entry) + "\n";
            File.AppendAllText(paths.HistoryFile, line);

            _fileEndsWithNewline = true;
            entries.Add(entry);
            return entry;
        }
    }

    public IReadOnlyList<HistoryEntry> Query(string? search, int limit)
    {
        lock (_lock)
        {
            var newestFirst = EnsureLoaded().AsEnumerable().Reverse();
            if (!string.IsNullOrWhiteSpace(search))
                newestFirst = newestFirst.Where(e => e.Text.Contains(search, StringComparison.OrdinalIgnoreCase));

            return newestFirst.Take(limit).ToList();
        }
    }

    public bool Delete(Guid id)
    {
        lock (_lock)
        {
            var entries = EnsureLoaded();
            var remaining = entries.Where(e => e.Id != id).ToList();
            if (remaining.Count == entries.Count)
                return false;

            // Memory only changes once the file has, so a failed write leaves both still agreeing.
            Rewrite(remaining);
            _entries = remaining;
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            EnsureLoaded();
            Rewrite([]);
            _entries = [];
        }
    }

    private List<HistoryEntry> EnsureLoaded() => _entries ??= ReadFile();

    private List<HistoryEntry> ReadFile()
    {
        var entries = new List<HistoryEntry>();
        if (!File.Exists(paths.HistoryFile))
            return entries;

        // Read errors (IOException) are deliberately not caught: treating an unreadable file as empty
        // would let the next Delete or Clear overwrite history that is still on disk.
        var text = File.ReadAllText(paths.HistoryFile);
        _fileEndsWithNewline = text.Length == 0 || text[^1] == '\n';

        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (TryParse(line, lineNumber) is { } entry)
                entries.Add(entry);
        }

        return entries;
    }

    private HistoryEntry? TryParse(string line, int lineNumber)
    {
        try
        {
            // A hand-edited line may omit fields, which deserialize to defaults rather than failing.
            if (JsonUtils.Deserialize(line, typeof(HistoryEntry)) is HistoryEntry { Text: not null } entry
                && entry.Id != Guid.Empty)
            {
                return entry with { TargetApp = entry.TargetApp ?? "" };
            }

            _logger.Warning("Skipping incomplete entry on line {LineNumber} of {Path}", lineNumber, paths.HistoryFile);
        }
        catch (JsonException ex)
        {
            _logger.Warning(ex, "Skipping corrupt entry on line {LineNumber} of {Path}", lineNumber, paths.HistoryFile);
        }

        return null;
    }

    private void Rewrite(IReadOnlyList<HistoryEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.HistoryFile)!);

        var tempPath = paths.HistoryFile + ".tmp";
        File.WriteAllText(tempPath, string.Concat(entries.Select(e => JsonUtils.Serialize(e) + "\n")));
        File.Move(tempPath, paths.HistoryFile, overwrite: true);
        _fileEndsWithNewline = true;
    }
}
