using Microsoft.Extensions.Time.Testing;
using Murmur.Common;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Shouldly;

namespace Murmur.Core.Tests.Services;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "murmur-tests-" + Guid.NewGuid().ToString("N"));
    private readonly MurmurPaths _paths;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));

    public HistoryStoreTests() => _paths = new MurmurPaths(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private HistoryStore CreateStore() => new(_paths, _time);

    [Fact]
    public void Add_ReturnsEntryWithIdAndTimestamp()
    {
        var entry = CreateStore().Add("hello", "notepad.exe", pasted: true);

        entry.Id.ShouldNotBe(Guid.Empty);
        entry.Timestamp.ShouldBe(_time.GetUtcNow());
        entry.Text.ShouldBe("hello");
        entry.TargetApp.ShouldBe("notepad.exe");
        entry.Pasted.ShouldBeTrue();
    }

    [Fact]
    public void Add_ThenQuery_NewestFirst()
    {
        var store = CreateStore();
        store.Add("one", "app", pasted: true);
        _time.Advance(TimeSpan.FromSeconds(1));
        store.Add("two", "app", pasted: true);

        store.Query(null, 10, null).Entries.Select(e => e.Text).ShouldBe(["two", "one"]);
    }

    [Fact]
    public void Add_PersistsToDiskAcrossInstances()
    {
        var added = CreateStore().Add("persisted", "code.exe", pasted: false);

        var loaded = CreateStore().Query(null, 10, null).Entries.Single();

        loaded.ShouldBe(added);
    }

    [Fact]
    public void Query_SearchIsCaseInsensitiveSubstring()
    {
        var store = CreateStore();
        store.Add("Hello World", "app", pasted: true);
        store.Add("bye", "app", pasted: true);

        store.Query("WORLD", 10, null).Entries.Single().Text.ShouldBe("Hello World");
    }

    [Fact]
    public void Query_BlankSearch_ReturnsEverything()
    {
        var store = CreateStore();
        store.Add("a", "app", pasted: true);
        store.Add("b", "app", pasted: true);

        store.Query("  ", 10, null).Entries.Count.ShouldBe(2);
    }

    [Fact]
    public void Query_RespectsLimit()
    {
        var store = CreateStore();
        for (var i = 0; i < 5; i++)
        {
            store.Add($"entry {i}", "app", pasted: true);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        var result = store.Query(null, 2, null);

        result.Entries.Count.ShouldBe(2);
        result.Entries.Select(e => e.Text).ShouldBe(["entry 4", "entry 3"]);
    }

    private HistoryEntry[] AddEntries(HistoryStore store, int count, string prefix = "entry")
    {
        var added = new HistoryEntry[count];
        for (var i = 0; i < count; i++)
        {
            added[i] = store.Add($"{prefix} {i}", "app", pasted: true);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        return added;
    }

    [Fact]
    public void Query_Before_ReturnsTheNextOlderPage()
    {
        var store = CreateStore();
        AddEntries(store, 5);

        var first = store.Query(null, 2, null);
        var second = store.Query(null, 2, first.Entries[^1].Id);

        first.Entries.Select(e => e.Text).ShouldBe(["entry 4", "entry 3"]);
        first.HasMore.ShouldBeTrue();
        second.Entries.Select(e => e.Text).ShouldBe(["entry 2", "entry 1"]);
        second.HasMore.ShouldBeTrue();
    }

    [Fact]
    public void Query_LastPage_ReportsNoMore()
    {
        var store = CreateStore();
        AddEntries(store, 5);

        var first = store.Query(null, 2, null);
        var second = store.Query(null, 2, first.Entries[^1].Id);
        var last = store.Query(null, 2, second.Entries[^1].Id);

        last.Entries.Select(e => e.Text).ShouldBe(["entry 0"]);
        last.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void Query_ExactlyOnePageOfEntries_ReportsNoMore()
    {
        var store = CreateStore();
        AddEntries(store, 3);

        var page = store.Query(null, 3, null);

        page.Entries.Count.ShouldBe(3);
        page.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void Query_Empty_ReportsNoMore()
    {
        var page = CreateStore().Query(null, 200, null);

        page.Entries.ShouldBeEmpty();
        page.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void Query_Before_IsStableWhenNewEntriesArrive()
    {
        var store = CreateStore();
        AddEntries(store, 5);
        var first = store.Query(null, 2, null);

        // Dictations made while the user is reading page 1 must not shift page 2.
        AddEntries(store, 3, "late");
        var second = store.Query(null, 2, first.Entries[^1].Id);
        var third = store.Query(null, 2, second.Entries[^1].Id);

        var seen = first.Entries.Concat(second.Entries).Concat(third.Entries).Select(e => e.Text).ToList();
        seen.ShouldBe(["entry 4", "entry 3", "entry 2", "entry 1", "entry 0"]);
        third.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void Query_Before_TiedTimestamps_NeitherRepeatsNorSkips()
    {
        var store = CreateStore();
        for (var i = 0; i < 5; i++)
            store.Add($"entry {i}", "app", pasted: true); // the clock never moves, so every timestamp is equal

        var first = store.Query(null, 2, null);
        var second = store.Query(null, 2, first.Entries[^1].Id);
        var third = store.Query(null, 2, second.Entries[^1].Id);

        first.Entries.Concat(second.Entries).Concat(third.Entries).Select(e => e.Text)
            .ShouldBe(["entry 4", "entry 3", "entry 2", "entry 1", "entry 0"]);
    }

    [Fact]
    public void Query_Before_AppliesTheSearchWithinTheOlderEntries()
    {
        var store = CreateStore();
        foreach (var text in new[] { "cat one", "dog one", "cat two", "dog two", "cat three" })
        {
            store.Add(text, "app", pasted: true);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        var first = store.Query("cat", 1, null);
        var second = store.Query("cat", 1, first.Entries[^1].Id);
        var third = store.Query("cat", 1, second.Entries[^1].Id);

        first.Entries.Single().Text.ShouldBe("cat three");
        first.HasMore.ShouldBeTrue();
        second.Entries.Single().Text.ShouldBe("cat two");
        second.HasMore.ShouldBeTrue();
        third.Entries.Single().Text.ShouldBe("cat one");
        third.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void Query_Before_AnEntryThatDoesNotMatchTheSearch_StillMarksThePosition()
    {
        var store = CreateStore();
        var added = AddEntries(store, 4); // entry 0 .. entry 3

        // The cursor is entry 2, which "entry 1" does not match; the search resumes just below it all the same.
        var page = store.Query("entry 1", 10, added[2].Id);

        page.Entries.Select(e => e.Text).ShouldBe(["entry 1"]);
        page.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void Query_Before_UnknownId_ReturnsAnEmptyPageWithoutThrowing()
    {
        var store = CreateStore();
        AddEntries(store, 3);

        var page = store.Query(null, 2, Guid.NewGuid());

        page.Entries.ShouldBeEmpty();
        page.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void Query_Before_TheEntryWasDeletedMeanwhile_ReturnsAnEmptyPageWithoutThrowing()
    {
        var store = CreateStore();
        var added = AddEntries(store, 3);
        store.Delete(added[1].Id).ShouldBeTrue();

        var page = store.Query(null, 2, added[1].Id);

        page.Entries.ShouldBeEmpty();
        page.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void Query_Before_TheOldestEntry_ReturnsAnEmptyPage()
    {
        var store = CreateStore();
        var added = AddEntries(store, 3);

        var page = store.Query(null, 2, added[0].Id);

        page.Entries.ShouldBeEmpty();
        page.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void Query_ZeroLimit_ReturnsNoEntriesButSaysThereAreMore()
    {
        var store = CreateStore();
        AddEntries(store, 2);

        var page = store.Query(null, 0, null);

        page.Entries.ShouldBeEmpty();
        page.HasMore.ShouldBeTrue();
    }

    [Fact]
    public void Delete_RemovesAndPersists()
    {
        var store = CreateStore();
        var keep = store.Add("keep", "app", pasted: true);
        var drop = store.Add("drop", "app", pasted: true);

        store.Delete(drop.Id).ShouldBeTrue();
        store.Delete(Guid.NewGuid()).ShouldBeFalse();

        var reloaded = CreateStore().Query(null, 10, null).Entries;
        reloaded.Select(e => e.Id).ShouldBe([keep.Id]);
        File.Exists(_paths.HistoryFile + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void Clear_RemovesAllAndPersists()
    {
        var store = CreateStore();
        store.Add("one", "app", pasted: true);
        store.Add("two", "app", pasted: true);

        store.Clear();

        store.Query(null, 10, null).Entries.ShouldBeEmpty();
        CreateStore().Query(null, 10, null).Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Clear_ThenAdd_StartsFresh()
    {
        var store = CreateStore();
        store.Add("old", "app", pasted: true);
        store.Clear();

        store.Add("new", "app", pasted: true);

        CreateStore().Query(null, 10, null).Entries.Select(e => e.Text).ShouldBe(["new"]);
    }

    [Fact]
    public void Clear_WithNoFile_DoesNotThrow()
    {
        Should.NotThrow(() => CreateStore().Clear());
    }

    [Fact]
    public void Load_SkipsCorruptLines()
    {
        var first = new HistoryEntry(Guid.NewGuid(), _time.GetUtcNow(), "first", "app", true);
        var second = new HistoryEntry(Guid.NewGuid(), _time.GetUtcNow().AddSeconds(1), "second", "app", false);
        Directory.CreateDirectory(_root);
        File.WriteAllLines(_paths.HistoryFile, [JsonUtils.Serialize(first), "garbage", "", JsonUtils.Serialize(second)]);

        var entries = CreateStore().Query(null, 10, null).Entries;

        entries.Count.ShouldBe(2);
        entries.Select(e => e.Text).ShouldBe(["second", "first"]);
    }

    [Fact]
    public void Add_AfterCorruptLoad_KeepsValidEntries()
    {
        var first = new HistoryEntry(Guid.NewGuid(), _time.GetUtcNow(), "first", "app", true);
        Directory.CreateDirectory(_root);
        File.WriteAllLines(_paths.HistoryFile, [JsonUtils.Serialize(first), "garbage"]);

        var store = CreateStore();
        _time.Advance(TimeSpan.FromSeconds(1));
        store.Add("second", "app", pasted: true);

        CreateStore().Query(null, 10, null).Entries.Select(e => e.Text).ShouldBe(["second", "first"]);
    }

    [Fact]
    public void Load_SkipsIncompleteEntries()
    {
        var valid = new HistoryEntry(Guid.NewGuid(), _time.GetUtcNow(), "valid", "app", true);
        Directory.CreateDirectory(_root);
        File.WriteAllLines(_paths.HistoryFile, ["{}", "null", JsonUtils.Serialize(valid)]);

        CreateStore().Query(null, 10, null).Entries.Select(e => e.Text).ShouldBe(["valid"]);
    }

    [Fact]
    public void Add_WhenFileLacksTrailingNewline_StartsNewLine()
    {
        var first = new HistoryEntry(Guid.NewGuid(), _time.GetUtcNow(), "first", "app", true);
        Directory.CreateDirectory(_root);
        File.WriteAllText(_paths.HistoryFile, JsonUtils.Serialize(first));

        var store = CreateStore();
        _time.Advance(TimeSpan.FromSeconds(1));
        store.Add("second", "app", pasted: true);

        CreateStore().Query(null, 10, null).Entries.Select(e => e.Text).ShouldBe(["second", "first"]);
    }

    [Fact]
    public void Add_UnicodeAndNewlinesInText_RoundTrips()
    {
        const string text = "line one\nline two \"quoted\" — café 日本語";
        CreateStore().Add(text, "app", pasted: true);

        CreateStore().Query(null, 10, null).Entries.Single().Text.ShouldBe(text);
        File.ReadAllLines(_paths.HistoryFile).Length.ShouldBe(1);
    }
}
