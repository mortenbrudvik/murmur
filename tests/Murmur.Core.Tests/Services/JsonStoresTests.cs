using Murmur.Common;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Shouldly;

namespace Murmur.Core.Tests.Services;

public sealed class JsonStoresTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "murmur-tests-" + Guid.NewGuid().ToString("N"));
    private readonly MurmurPaths _paths;

    public JsonStoresTests() => _paths = new MurmurPaths(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Settings_MissingFile_ReturnsDefaults()
    {
        new SettingsService(_paths).Load().ShouldBe(MurmurSettings.Default);
    }

    [Fact]
    public void Settings_SaveThenLoad_RoundTrips()
    {
        var service = new SettingsService(_paths);
        var settings = new MurmurSettings(
            new HotkeyDefinition(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Space"),
            MicrophoneId: "mic-1",
            KeepAudio: true);

        service.Save(settings);
        var loaded = new SettingsService(_paths).Load();

        loaded.Hotkey.VirtualKey.ShouldBe(0x20);
        loaded.Hotkey.Modifiers.ShouldBe(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
        loaded.Hotkey.DisplayName.ShouldBe("Ctrl+Shift+Space");
        loaded.MicrophoneId.ShouldBe("mic-1");
        loaded.KeepAudio.ShouldBeTrue();
        loaded.SoundCues.ShouldBeTrue();
        loaded.StartWithWindows.ShouldBeFalse();
        File.Exists(_paths.SettingsFile + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void Settings_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_paths.SettingsFile, "{not json");

        new SettingsService(_paths).Load().ShouldBe(MurmurSettings.Default);
    }

    [Fact]
    public void Settings_InvalidHotkeyOnDisk_FallsBackToDefault()
    {
        var service = new SettingsService(_paths);
        service.Save(MurmurSettings.Default with
        {
            Hotkey = new HotkeyDefinition(0x41, HotkeyModifiers.None, "A"),
            MicrophoneId = "mic-2",
        });

        var loaded = service.Load();

        loaded.Hotkey.ShouldBe(HotkeyDefinition.Default);
        loaded.MicrophoneId.ShouldBe("mic-2");
    }

    [Fact]
    public void Settings_MissingHotkeyOnDisk_FallsBackToDefault()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_paths.SettingsFile, "{\"microphoneId\":\"mic-3\"}");

        var loaded = new SettingsService(_paths).Load();

        loaded.Hotkey.ShouldBe(HotkeyDefinition.Default);
        loaded.MicrophoneId.ShouldBe("mic-3");
    }

    [Fact]
    public void Settings_Save_WritesCamelCaseJsonThroughJsonUtils()
    {
        new SettingsService(_paths).Save(MurmurSettings.Default);

        var json = File.ReadAllText(_paths.SettingsFile);

        json.ShouldContain("\"hotkey\"");
        json.ShouldContain("\"virtualKey\"");
        json.ShouldContain("\"modifiers\":\"none\"");
        json.ShouldNotContain("isValid");
        JsonUtils.Deserialize(json, typeof(MurmurSettings)).ShouldBe(MurmurSettings.Default);
    }

    [Fact]
    public void Vocabulary_SaveThenLoad_RoundTrips()
    {
        var service = new VocabularyService(_paths);
        service.Save(new Vocabulary(
            ["Murmur", "WebView2"],
            [new ReplacementRule("web view two", "WebView2")]));

        var loaded = new VocabularyService(_paths).Load();

        loaded.HintWords.ShouldBe(["Murmur", "WebView2"]);
        loaded.Replacements.Count.ShouldBe(1);
        loaded.Replacements[0].ShouldBe(new ReplacementRule("web view two", "WebView2"));
    }

    [Fact]
    public void Vocabulary_MissingFile_ReturnsEmpty()
    {
        var loaded = new VocabularyService(_paths).Load();

        loaded.HintWords.ShouldBeEmpty();
        loaded.Replacements.ShouldBeEmpty();
    }

    [Fact]
    public void Vocabulary_CorruptFile_ReturnsEmpty()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_paths.VocabularyFile, "[[[ not json");

        var loaded = new VocabularyService(_paths).Load();

        loaded.HintWords.ShouldBeEmpty();
        loaded.Replacements.ShouldBeEmpty();
    }

    [Fact]
    public void Vocabulary_PartialFile_NormalizesNullLists()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_paths.VocabularyFile, "{\"hintWords\":[\"x\"]}");

        var loaded = new VocabularyService(_paths).Load();

        loaded.HintWords.ShouldBe(["x"]);
        loaded.Replacements.ShouldNotBeNull();
        loaded.Replacements.ShouldBeEmpty();
    }

    [Fact]
    public void Paths_AreUnderRootWithSpecifiedNames()
    {
        _paths.SettingsFile.ShouldBe(Path.Combine(_root, "settings.json"));
        _paths.VocabularyFile.ShouldBe(Path.Combine(_root, "vocabulary.json"));
        _paths.HistoryFile.ShouldBe(Path.Combine(_root, "history.jsonl"));
        _paths.ModelsDirectory.ShouldBe(Path.Combine(_root, "models"));
        _paths.AudioDirectory.ShouldBe(Path.Combine(_root, "audio"));
        _paths.LogsDirectory.ShouldBe(Path.Combine(_root, "Logs"));
    }

    [Fact]
    public void Paths_Default_IsLocalAppDataMurmur()
    {
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur");

        MurmurPaths.Default.SettingsFile.ShouldBe(Path.Combine(expectedRoot, "settings.json"));
    }
}
