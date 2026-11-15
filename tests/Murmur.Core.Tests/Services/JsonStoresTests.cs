using Murmur.Common;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Tests.Helpers;
using Murmur.Core.Tests.Input;
using Shouldly;

namespace Murmur.Core.Tests.Services;

public sealed class JsonStoresTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly MurmurPaths _paths;

    public JsonStoresTests() => _paths = new MurmurPaths(_temp.Path);

    public void Dispose() => _temp.Dispose();

    private SettingsService NewSettingsService(IKeyboardLayout? layout = null) => new(_paths, layout ?? FakeKeyboardLayout.Unmapped);

    [Fact]
    public void Settings_MissingFile_ReturnsDefaults()
    {
        NewSettingsService().Load().ShouldBe(MurmurSettings.Default);
    }

    [Fact]
    public void Settings_UpdateThenLoad_RoundTrips()
    {
        var service = NewSettingsService();
        var settings = new MurmurSettings(
            new HotkeyDefinition(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Space"),
            MicrophoneId: "mic-1",
            KeepAudio: true);

        service.Update(_ => settings);
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
        Directory.CreateDirectory(_temp.Path);
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
        loaded.KeepMicrophoneReady.ShouldBeFalse(); // opt-in: an upgrade never starts holding the microphone open
    }

    [Fact]
    public void Settings_KeepMicrophoneReady_RoundTrips()
    {
        NewSettingsService().Update(_ => MurmurSettings.Default with { KeepMicrophoneReady = true });

        NewSettingsService().Load().KeepMicrophoneReady.ShouldBeTrue();
    }

    [Fact]
    public void SettingsService_UpdateThenLoad_RoundTripsTheNewFields()
    {
        var service = NewSettingsService();
        service.Update(_ => MurmurSettings.Default with
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
        service.Update(_ => MurmurSettings.Default with { Hotkey = new HotkeyDefinition(0x41, HotkeyModifiers.None, "A") });
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
        NewSettingsService().Update(_ => MurmurSettings.Default with
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
        NewSettingsService().Update(_ => MurmurSettings.Default with
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
        Directory.CreateDirectory(_temp.Path);
        File.WriteAllText(_paths.SettingsFile, "{not json");

        NewSettingsService().Load().ShouldBe(MurmurSettings.Default);
    }

    [Fact]
    public void Settings_InvalidHotkeyOnDisk_FallsBackToDefault()
    {
        var service = NewSettingsService();
        service.Update(_ => MurmurSettings.Default with
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
        Directory.CreateDirectory(_temp.Path);
        File.WriteAllText(_paths.SettingsFile, "{\"microphoneId\":\"mic-3\"}");

        var loaded = NewSettingsService().Load();

        loaded.Hotkey.ShouldBe(HotkeyDefinition.Default);
        loaded.MicrophoneId.ShouldBe("mic-3");
    }

    [Fact]
    public void Settings_Update_WritesCamelCaseJsonThroughJsonUtils()
    {
        NewSettingsService().Update(_ => MurmurSettings.Default);

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
        Directory.CreateDirectory(_temp.Path);
        File.WriteAllText(_paths.VocabularyFile, "[[[ not json");

        var loaded = new VocabularyService(_paths).Load();

        loaded.HintWords.ShouldBeEmpty();
        loaded.Replacements.ShouldBeEmpty();
    }

    [Fact]
    public void Vocabulary_PartialFile_NormalizesNullLists()
    {
        Directory.CreateDirectory(_temp.Path);
        File.WriteAllText(_paths.VocabularyFile, "{\"hintWords\":[\"x\"]}");

        var loaded = new VocabularyService(_paths).Load();

        loaded.HintWords.ShouldBe(["x"]);
        loaded.Replacements.ShouldNotBeNull();
        loaded.Replacements.ShouldBeEmpty();
    }

    [Fact]
    public void SettingsService_Load_KeepsTheFileItReadUntilASave()
    {
        var service = NewSettingsService();
        service.Update(_ => MurmurSettings.Default with { MicrophoneId = "mic-1" });
        service.Load();

        // Changed behind the service's back: it keeps what it had.
        File.WriteAllText(_paths.SettingsFile, "{\"microphoneId\":\"mic-2\"}");

        service.Load().MicrophoneId.ShouldBe("mic-1");
    }

    [Fact]
    public void SettingsService_Load_ReadsTheFileOnce()
    {
        Directory.CreateDirectory(_temp.Path);
        File.WriteAllText(_paths.SettingsFile, "{\"microphoneId\":\"mic-1\"}");
        var service = NewSettingsService();
        service.Load();
        File.Delete(_paths.SettingsFile);

        service.Load().MicrophoneId.ShouldBe("mic-1");
    }

    [Fact]
    public void SettingsService_Update_ReplacesTheKeptSettings()
    {
        var service = NewSettingsService();
        service.Load();

        service.Update(_ => MurmurSettings.Default with { MicrophoneId = "mic-1" });
        service.Load().MicrophoneId.ShouldBe("mic-1");

        service.Update(s => s with { KeepAudio = true });
        service.Load().ShouldBe(MurmurSettings.Default with { MicrophoneId = "mic-1", KeepAudio = true });
    }

    [Fact]
    public void SettingsService_ReadFails_KeepsNothingAndReadsAgainNextTime()
    {
        Directory.CreateDirectory(_temp.Path);
        File.WriteAllText(_paths.SettingsFile, "{\"microphoneId\":\"mic-1\"}");
        var service = NewSettingsService();

        using (new FileStream(_paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
            Should.Throw<IOException>(() => service.Load());

        service.Load().MicrophoneId.ShouldBe("mic-1");
    }

    [Fact]
    public void SettingsService_KeptSettings_StillRepairTheHotkeyAndNameItAfterTheLayout()
    {
        var service = NewSettingsService(FakeKeyboardLayout.Nordic);
        service.Update(_ => MurmurSettings.Default with
        {
            Hotkey = new HotkeyDefinition(0xDD, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Ctrl+Alt+]"),
        });

        service.Load().Hotkey.DisplayName.ShouldBe("Ctrl+Alt+Å");

        service.Update(_ => MurmurSettings.Default with { Hotkey = new HotkeyDefinition(0x41, HotkeyModifiers.None, "A") });

        service.Load().Hotkey.ShouldBe(HotkeyDefinition.Default);
    }

    [Fact]
    public void VocabularyService_Load_KeepsTheFileItReadUntilASave()
    {
        var service = new VocabularyService(_paths);
        service.Save(new Vocabulary(["Murmur"], []));
        service.Load();

        File.WriteAllText(_paths.VocabularyFile, "{\"hintWords\":[\"Changed\"]}");
        service.Load().HintWords.ShouldBe(["Murmur"]);

        service.Save(new Vocabulary(["WebView2"], [new ReplacementRule("web view two", "WebView2")]));
        var loaded = service.Load();

        loaded.HintWords.ShouldBe(["WebView2"]);
        loaded.Replacements.ShouldBe([new ReplacementRule("web view two", "WebView2")]);
    }

    [Fact]
    public void VocabularyService_SaveWithMissingLists_KeepsThemEmpty()
    {
        var service = new VocabularyService(_paths);

        service.Save(new Vocabulary(null!, null!));

        service.Load().HintWords.ShouldBeEmpty();
        service.Load().Replacements.ShouldBeEmpty();
    }

    [Fact]
    public void VocabularyService_ReadFails_KeepsNothingAndReadsAgainNextTime()
    {
        new VocabularyService(_paths).Save(new Vocabulary(["Murmur"], []));
        var service = new VocabularyService(_paths);

        using (new FileStream(_paths.VocabularyFile, FileMode.Open, FileAccess.Read, FileShare.None))
            Should.Throw<IOException>(() => service.Load());

        service.Load().HintWords.ShouldBe(["Murmur"]);
    }

    [Fact]
    public void Paths_AreUnderRootWithSpecifiedNames()
    {
        _paths.SettingsFile.ShouldBe(Path.Combine(_temp.Path, "settings.json"));
        _paths.VocabularyFile.ShouldBe(Path.Combine(_temp.Path, "vocabulary.json"));
        _paths.HistoryFile.ShouldBe(Path.Combine(_temp.Path, "history.jsonl"));
        _paths.ModelsDirectory.ShouldBe(Path.Combine(_temp.Path, "models"));
        _paths.AudioDirectory.ShouldBe(Path.Combine(_temp.Path, "audio"));
        _paths.LogsDirectory.ShouldBe(Path.Combine(_temp.Path, "Logs"));
        _paths.WebView2Directory.ShouldBe(Path.Combine(_temp.Path, "WebView2"));
    }

    [Fact]
    public void Paths_Default_IsLocalAppDataMurmur()
    {
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur");

        MurmurPaths.Default.SettingsFile.ShouldBe(Path.Combine(expectedRoot, "settings.json"));
    }
}
