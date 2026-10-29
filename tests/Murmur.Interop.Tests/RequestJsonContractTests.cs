using Murmur.Common;
using Murmur.Core.Features.History;
using Murmur.Core.Features.Settings;
using Murmur.Core.Features.Vocabulary;
using Murmur.Core.Models;
using Shouldly;

namespace Murmur.Interop.Tests;

/// <summary>
/// Pins the JSON the React side sends and receives (camelCase names, string enums, string Guids) by running it through
/// <see cref="JsonUtils"/>, exactly as <see cref="ApiBridge"/> does.
/// </summary>
public class RequestJsonContractTests
{
    [Fact]
    public void UpdateSettingsRequest_DeserializesTheCamelCaseJsonJsSends()
    {
        const string json = """
            {"hotkey":{"virtualKey":32,"modifiers":"ctrl, shift","displayName":"Ctrl+Shift+Space"},"microphoneId":null,"soundCues":true,"startWithWindows":false,"keepAudio":false}
            """;

        var request = (UpdateSettingsRequest?)JsonUtils.Deserialize(json, typeof(UpdateSettingsRequest));

        request.ShouldNotBeNull();
        request.Hotkey.VirtualKey.ShouldBe(32);
        request.Hotkey.Modifiers.ShouldBe(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
        request.Hotkey.DisplayName.ShouldBe("Ctrl+Shift+Space");
        request.Hotkey.IsValid.ShouldBeTrue();
        request.MicrophoneId.ShouldBeNull();
        request.SoundCues.ShouldBeTrue();
        request.StartWithWindows.ShouldBeFalse();
        request.KeepAudio.ShouldBeFalse();
    }

    [Fact]
    public void UpdateSettingsRequest_ModifierNamesAreCamelCaseStrings()
    {
        const string json = """
            {"hotkey":{"virtualKey":163,"modifiers":"none","displayName":"Right Ctrl"},"microphoneId":"{0.0.1.00000000}.abc","soundCues":false,"startWithWindows":true,"keepAudio":true}
            """;

        var request = (UpdateSettingsRequest?)JsonUtils.Deserialize(json, typeof(UpdateSettingsRequest));

        request.ShouldNotBeNull();
        request.Hotkey.Modifiers.ShouldBe(HotkeyModifiers.None);
        request.MicrophoneId.ShouldBe("{0.0.1.00000000}.abc");
        request.KeepAudio.ShouldBeTrue();
    }

    [Fact]
    public void GetHistoryRequest_EmptyParams_UseTheDefaults()
    {
        var request = (GetHistoryRequest?)JsonUtils.Deserialize("{}", typeof(GetHistoryRequest));

        request.ShouldBe(new GetHistoryRequest(null, 200));
    }

    [Fact]
    public void GetHistoryRequest_ReadsSearchAndLimit()
    {
        var request = (GetHistoryRequest?)JsonUtils.Deserialize("""{"search":"abc","limit":50}""", typeof(GetHistoryRequest));

        request.ShouldBe(new GetHistoryRequest("abc", 50));
    }

    [Fact]
    public void DeleteHistoryEntryRequest_ReadsTheIdFromAString()
    {
        var id = Guid.NewGuid();

        var request = (DeleteHistoryEntryRequest?)JsonUtils.Deserialize($$"""{"id":"{{id}}"}""", typeof(DeleteHistoryEntryRequest));

        request.ShouldBe(new DeleteHistoryEntryRequest(id));
    }

    [Fact]
    public void UpdateVocabularyRequest_ReadsHintWordsAndReplacements()
    {
        const string json = """
            {"hintWords":["Murmur","WPF"],"replacements":[{"spoken":"web view","written":"WebView"}]}
            """;

        var request = (UpdateVocabularyRequest?)JsonUtils.Deserialize(json, typeof(UpdateVocabularyRequest));

        request.ShouldNotBeNull();
        request.HintWords.ShouldBe(["Murmur", "WPF"]);
        request.Replacements.ShouldBe([new ReplacementRule("web view", "WebView")]);
    }

    [Fact]
    public void HistoryEntry_SerializesWithCamelCaseNamesAndAStringId()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var entry = new HistoryEntry(id, new DateTimeOffset(2026, 10, 30, 8, 15, 0, TimeSpan.Zero), "hello", "notepad.exe", true);

        var json = JsonUtils.Serialize(entry);

        json.ShouldContain("\"id\":\"11111111-2222-3333-4444-555555555555\"");
        json.ShouldContain("\"timestamp\":\"2026-10-30T08:15:00+00:00\"");
        json.ShouldContain("\"text\":\"hello\"");
        json.ShouldContain("\"targetApp\":\"notepad.exe\"");
        json.ShouldContain("\"pasted\":true");
    }

    [Fact]
    public void EngineStatus_SerializesTheStateAsACamelCaseString()
    {
        var json = JsonUtils.Serialize(new EngineStatus(EngineState.NotStarted, "ggml-large-v3-turbo", DownloadProgress: 0.5));

        json.ShouldContain("\"state\":\"notStarted\"");
        json.ShouldContain("\"modelName\":\"ggml-large-v3-turbo\"");
        json.ShouldContain("\"downloadProgress\":0.5");
    }

    [Fact]
    public void MurmurSettings_SerializesTheHotkeyModifiersAsNames()
    {
        var settings = new MurmurSettings(new HotkeyDefinition(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Space"));

        var json = JsonUtils.Serialize(settings);

        json.ShouldContain("\"modifiers\":\"ctrl, shift\"");
        json.ShouldContain("\"microphoneId\":null");
        json.ShouldContain("\"soundCues\":true");
    }
}
