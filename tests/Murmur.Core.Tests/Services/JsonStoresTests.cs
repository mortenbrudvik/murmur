using Murmur.Common;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Tests.Input;
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

    private SettingsService NewSettingsService(IKeyboardLayout? layout = null) => new(_paths, layout ?? FakeKeyboardLayout.Unmapped);

    [Fact]
    public void Settings_MissingFile_ReturnsDefaults()
    {
        NewSettingsService().Load().ShouldBe(MurmurSettings.Default);
    }

    [Fact]
    public void Settings_SaveThenLoad_RoundTrips()
    {
        var service = NewSettingsService();
        var settings = new MurmurSettings(
            new HotkeyDefinition(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Space"),
            MicrophoneId: "mic-1",
            KeepAudio: true);

        service.Save(settings);
        var loaded = NewSettingsService().Load();

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
    public void SettingsService_LoadVersion10File_DefaultsTheNewFields()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            _paths.SettingsFile,
            "{\"hotkey\":{\"virtualKey\":163,\"modifiers\":\"none\",\"displayName\":\"Right Ctrl\"},\"microphoneId\":null,"
            + "\"soundCues\":true,\"startWithWindows\":false,\"keepAudio\":false}");

        var loaded = NewSettingsService().Load();

        loaded.Hotkey.ShouldBe(HotkeyDefinition.Default);
        loaded.SpeechModel.ShouldBeNull();
        loaded.SetupStep.ShouldBeNull();
        loaded.SetupCompleted.ShouldBeFalse();
        loaded.CudaTipDismissed.ShouldBeFalse();
    }

    [Fact]
    public void SettingsService_SaveThenLoad_RoundTripsTheNewFields()
    {
        var service = NewSettingsService();
        service.Save(MurmurSettings.Default with
        {
            SpeechModel = SpeechModelId.SmallEn,
            SetupStep = SetupStep.TryIt,
            SetupCompleted = true,
            CudaTipDismissed = true,
        });

        var loaded = NewSettingsService().Load();

        loaded.SpeechModel.ShouldBe(SpeechModelId.SmallEn);
        loaded.SetupStep.ShouldBe(SetupStep.TryIt);
        loaded.SetupCompleted.ShouldBeTrue();
        loaded.CudaTipDismissed.ShouldBeTrue();
        var json = File.ReadAllText(_paths.SettingsFile);
        json.ShouldContain("\"speechModel\":\"smallEn\"");
        json.ShouldContain("\"setupStep\":\"tryIt\"");
    }

    [Fact]
    public async Task SettingsService_UpdateFromManyThreads_LosesNoChange()
    {
        var service = NewSettingsService();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            service.Update(s => s with { MicrophoneId = (int.Parse(s.MicrophoneId ?? "0") + 1).ToString() }))));

        service.Load().MicrophoneId.ShouldBe("50");
    }

    [Fact]
    public void SettingsService_Update_SavesAndReturnsTheChangedSettings()
    {
        var service = NewSettingsService();

        var returned = service.Update(s => s with { SoundCues = false, SetupStep = SetupStep.Microphone });

        returned.SoundCues.ShouldBeFalse();
        returned.SetupStep.ShouldBe(SetupStep.Microphone);
        NewSettingsService().Load().ShouldBe(returned);
    }

    [Fact]
    public void SettingsService_UpdateOverAnInvalidStoredHotkey_HandsTheChangeTheRepairedHotkey()
    {
        var service = NewSettingsService();
        service.Save(MurmurSettings.Default with { Hotkey = new HotkeyDefinition(0x41, HotkeyModifiers.None, "A") });
        HotkeyDefinition? seen = null;

        var returned = service.Update(s =>
        {
            seen = s.Hotkey;
            return s with { KeepAudio = true };
        });

        seen.ShouldBe(HotkeyDefinition.Default);
        returned.Hotkey.ShouldBe(HotkeyDefinition.Default);
        NewSettingsService().Load().Hotkey.ShouldBe(HotkeyDefinition.Default);
    }

    [Fact]
    public void SettingsService_LoadOemHotkey_RecomputesTheDisplayName()
    {
        // Saved by a build that named symbol keys after the US layout: the key right of P is "å" here, not "]".
        NewSettingsService().Save(MurmurSettings.Default with
        {
            Hotkey = new HotkeyDefinition(0xDD, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Ctrl+Alt+]"),
        });

        var loaded = NewSettingsService(FakeKeyboardLayout.Nordic).Load();

        loaded.Hotkey.ShouldBe(new HotkeyDefinition(0xDD, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Ctrl+Alt+Å"));
    }

    [Fact]
    public void SettingsService_LoadHotkeyThatIsNotAnOemKey_KeepsTheSavedDisplayNameAndSkipsTheLayout()
    {
        var layout = new FakeKeyboardLayout();
        NewSettingsService().Save(MurmurSettings.Default with
        {
            Hotkey = new HotkeyDefinition(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Space"),
        });

        var loaded = NewSettingsService(layout).Load();

        loaded.Hotkey.DisplayName.ShouldBe("Ctrl+Shift+Space");
        layout.Asked.ShouldBeEmpty();
    }

    [Fact]
    public void Settings_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_paths.SettingsFile, "{not json");

        NewSettingsService().Load().ShouldBe(MurmurSettings.Default);
    }

    [Fact]
    public void Settings_InvalidHotkeyOnDisk_FallsBackToDefault()
    {
        var service = NewSettingsService();
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

        var loaded = NewSettingsService().Load();

        loaded.Hotkey.ShouldBe(HotkeyDefinition.Default);
        loaded.MicrophoneId.ShouldBe("mic-3");
    }

    [Fact]
    public void Settings_Save_WritesCamelCaseJsonThroughJsonUtils()
    {
        NewSettingsService().Save(MurmurSettings.Default);

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
