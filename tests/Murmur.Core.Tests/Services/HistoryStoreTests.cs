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

        store.Query(null, 10).Select(e => e.Text).ShouldBe(["two", "one"]);
    }

    [Fact]
    public void Add_PersistsToDiskAcrossInstances()
    {
        var added = CreateStore().Add("persisted", "code.exe", pasted: false);

        var loaded = CreateStore().Query(null, 10).Single();

        loaded.ShouldBe(added);
    }

    [Fact]
    public void Query_SearchIsCaseInsensitiveSubstring()
    {
        var store = CreateStore();
        store.Add("Hello World", "app", pasted: true);
        store.Add("bye", "app", pasted: true);

        store.Query("WORLD", 10).Single().Text.ShouldBe("Hello World");
    }

    [Fact]
    public void Query_BlankSearch_ReturnsEverything()
    {
        var store = CreateStore();
        store.Add("a", "app", pasted: true);
        store.Add("b", "app", pasted: true);

        store.Query("  ", 10).Count.ShouldBe(2);
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

        var result = store.Query(null, 2);

        result.Count.ShouldBe(2);
        result.Select(e => e.Text).ShouldBe(["entry 4", "entry 3"]);
    }

    [Fact]
    public void Delete_RemovesAndPersists()
    {
        var store = CreateStore();
        var keep = store.Add("keep", "app", pasted: true);
        var drop = store.Add("drop", "app", pasted: true);

        store.Delete(drop.Id).ShouldBeTrue();
        store.Delete(Guid.NewGuid()).ShouldBeFalse();

        var reloaded = CreateStore().Query(null, 10);
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

        store.Query(null, 10).ShouldBeEmpty();
        CreateStore().Query(null, 10).ShouldBeEmpty();
    }

    [Fact]
    public void Clear_ThenAdd_StartsFresh()
    {
        var store = CreateStore();
        store.Add("old", "app", pasted: true);
        store.Clear();

        store.Add("new", "app", pasted: true);

        CreateStore().Query(null, 10).Select(e => e.Text).ShouldBe(["new"]);
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

        var entries = CreateStore().Query(null, 10);

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

        CreateStore().Query(null, 10).Select(e => e.Text).ShouldBe(["second", "first"]);
    }

    [Fact]
    public void Load_SkipsIncompleteEntries()
    {
        var valid = new HistoryEntry(Guid.NewGuid(), _time.GetUtcNow(), "valid", "app", true);
        Directory.CreateDirectory(_root);
        File.WriteAllLines(_paths.HistoryFile, ["{}", "null", JsonUtils.Serialize(valid)]);

        CreateStore().Query(null, 10).Select(e => e.Text).ShouldBe(["valid"]);
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

        CreateStore().Query(null, 10).Select(e => e.Text).ShouldBe(["second", "first"]);
    }

    [Fact]
    public void Add_UnicodeAndNewlinesInText_RoundTrips()
    {
        const string text = "line one\nline two \"quoted\" — café 日本語";
        CreateStore().Add(text, "app", pasted: true);

        CreateStore().Query(null, 10).Single().Text.ShouldBe(text);
        File.ReadAllLines(_paths.HistoryFile).Length.ShouldBe(1);
    }
}
