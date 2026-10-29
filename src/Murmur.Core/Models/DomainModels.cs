using System.Text.Json.Serialization;

namespace Murmur.Core.Models;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}

public sealed record HotkeyDefinition(int VirtualKey, HotkeyModifiers Modifiers, string DisplayName)
{
    public static HotkeyDefinition Default { get; } = new(0xA3, HotkeyModifiers.None, "Right Ctrl");

    /// <summary>A lone Ctrl/Shift/Alt (left or right) or Win key, with no other modifier.</summary>
    [JsonIgnore]
    public bool IsModifierOnly =>
        Modifiers == HotkeyModifiers.None
        && (VirtualKey is >= 0xA0 and <= 0xA5 or 0x5B or 0x5C);

    /// <summary>Modifier-only, an F1-F24 key, or any key combined with at least one modifier.</summary>
    [JsonIgnore]
    public bool IsValid =>
        IsModifierOnly
        || VirtualKey is >= 0x70 and <= 0x87
        || Modifiers != HotkeyModifiers.None;
}

public sealed record MurmurSettings(
    HotkeyDefinition Hotkey,
    string? MicrophoneId = null,
    bool SoundCues = true,
    bool StartWithWindows = true,
    bool KeepAudio = false)
{
    public static MurmurSettings Default { get; } = new(HotkeyDefinition.Default);
}

public sealed record ReplacementRule(string Spoken, string Written);

public sealed record Vocabulary(IReadOnlyList<string> HintWords, IReadOnlyList<ReplacementRule> Replacements)
{
    public static Vocabulary Empty { get; } = new([], []);
}

public sealed record HistoryEntry(Guid Id, DateTimeOffset Timestamp, string Text, string TargetApp, bool Pasted);

public sealed record MicrophoneInfo(string Id, string Name, bool IsDefault);

public enum EngineState
{
    NotStarted,
    Downloading,
    Loading,
    Ready,
    Error
}

public sealed record EngineStatus(
    EngineState State,
    string ModelName,
    string? Runtime = null,
    double? DownloadProgress = null,
    string? Error = null);

public sealed record TranscriptionResult(string Text, float MaxNoSpeechProbability);
