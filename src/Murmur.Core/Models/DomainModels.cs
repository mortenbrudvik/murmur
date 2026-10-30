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

    /// <summary>
    /// A Windows shell shortcut that Murmur must never take over: Alt+Tab, Win+Tab (with any extra modifiers)
    /// and Alt+F4. The hotkey's main key is swallowed system-wide, so one of these would break window switching.
    /// </summary>
    [JsonIgnore]
    public bool IsReservedShortcut =>
        (VirtualKey == 0x09 && (Modifiers & (HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0)
        || (VirtualKey == 0x73 && Modifiers.HasFlag(HotkeyModifiers.Alt));

    /// <summary>Modifier-only, an F1-F24 key, or any key combined with at least one modifier — never a reserved shortcut.</summary>
    [JsonIgnore]
    public bool IsValid =>
        !IsReservedShortcut
        && (IsModifierOnly
            || VirtualKey is >= 0x70 and <= 0x87
            || Modifiers != HotkeyModifiers.None);
}

public enum SpeechModelId
{
    LargeV3Turbo,
    SmallEn
}

public enum SetupStep
{
    SpeechModel,
    Microphone,
    TryIt
}

public enum GpuAdviceState
{
    NotApplicable,
    CudaInUse,
    InstallCuda,
    UpdateDriver
}

public enum LinkTarget
{
    CudaDownload,
    NvidiaDriver,
    MicrophonePrivacy
}

public enum MicTestState
{
    Listening,
    AccessDenied,
    Failed
}

/// <param name="StartWithWindows">
/// Off by default: Murmur must not register itself for sign-in without being asked, which the Microsoft Store expects.
/// A saved settings file always carries the value, so this only decides it for a first run.
/// </param>
/// <param name="SpeechModel">The chosen speech model; null until it is first resolved.</param>
/// <param name="SetupStep">The setup step the user stopped at; null until the first-run decision is made.</param>
/// <param name="SetupCompleted">Setup was finished or skipped, so it is not shown again.</param>
/// <param name="CudaTipDismissed">The user closed the tip about installing CUDA for a faster GPU.</param>
public sealed record MurmurSettings(
    HotkeyDefinition Hotkey,
    string? MicrophoneId = null,
    bool SoundCues = true,
    bool StartWithWindows = false,
    bool KeepAudio = false,
    SpeechModelId? SpeechModel = null,
    SetupStep? SetupStep = null,
    bool SetupCompleted = false,
    bool CudaTipDismissed = false)
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

/// <param name="ModelName">The display name of the model the state is about ("Whisper large-v3-turbo"); empty before one is chosen.</param>
/// <param name="Runtime">The Whisper runtime the loaded model runs on ("Cuda", "Vulkan", "Cpu"), once Ready.</param>
/// <param name="Error">
/// Why the engine failed (with <see cref="EngineState.Error"/>), or why a model switch failed while the previous model
/// stays <see cref="EngineState.Ready"/>.
/// </param>
/// <param name="Download">
/// The download in progress: the model itself while <see cref="EngineState.Downloading"/>, or the next model while a
/// switch keeps the current one <see cref="EngineState.Ready"/>.
/// </param>
/// <param name="Gpu">The tip about CUDA for this PC; null until the engine has looked at the GPU.</param>
/// <param name="Switching">
/// True only on the <see cref="EngineState.Loading"/> status of a model switch, where <see cref="ModelName"/> is the
/// model being loaded and the previous one still transcribes; false for a first load.
/// </param>
public sealed record EngineStatus(
    EngineState State,
    string ModelName,
    string? Runtime = null,
    string? Error = null,
    DownloadProgress? Download = null,
    GpuAdvice? Gpu = null,
    bool Switching = false);

public sealed record TranscriptionResult(string Text, float MaxNoSpeechProbability);

/// <param name="Retries">Consecutive failed attempts of the current download; 0 while bytes flow.</param>
public sealed record DownloadProgress(
    string ModelName,
    long BytesReceived,
    long TotalBytes,
    double BytesPerSecond,
    double? SecondsRemaining,
    int Retries)
{
    /// <summary>Serialized so the web can show "Reconnecting" without knowing the retry rules.</summary>
    public bool Reconnecting => Retries > 0;
}

public sealed record GpuAdvice(GpuAdviceState State, string? GpuName)
{
    public static GpuAdvice None { get; } = new(GpuAdviceState.NotApplicable, null);
}

public sealed record SetupState(bool ShowSetup, SetupStep Step, bool CudaTipDismissed, string? OpenPage);

public sealed record SpeechModelOption(SpeechModelId Id, string Label, long SizeBytes);

public sealed record ModelChoice(SpeechModelId Selected, SpeechModelId Recommended, IReadOnlyList<SpeechModelOption> Options);

public sealed record MicTestResult(MicTestState State, string? Error = null);
