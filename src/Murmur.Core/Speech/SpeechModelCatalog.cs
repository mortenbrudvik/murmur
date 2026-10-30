using Murmur.Core.Models;

namespace Murmur.Core.Speech;

/// <param name="Sha256">Lowercase hex digest of the file at <paramref name="Url"/>.</param>
public sealed record SpeechModelInfo(SpeechModelId Id, string DisplayName, string FileName, long SizeBytes, string Sha256, Uri Url);

/// <summary>
/// The speech models Murmur can download. Every URL is pinned to one commit of the whisper.cpp model repository,
/// so the size and SHA-256 below describe exactly the bytes that are fetched.
/// </summary>
public static class SpeechModelCatalog
{
    private const string PinnedCommit = "5359861c739e955e79d9a303bcbc70fb988958b1";

    public static SpeechModelInfo LargeV3Turbo { get; } = new(
        SpeechModelId.LargeV3Turbo,
        "Whisper large-v3-turbo",
        "ggml-large-v3-turbo.bin",
        1_624_555_275,
        "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69",
        PinnedUrl("ggml-large-v3-turbo.bin"));

    public static SpeechModelInfo SmallEn { get; } = new(
        SpeechModelId.SmallEn,
        "Whisper small (English)",
        "ggml-small.en.bin",
        487_614_201,
        "c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d",
        PinnedUrl("ggml-small.en.bin"));

    /// <summary>What the model pickers offer, the larger and more accurate model first.</summary>
    public static IReadOnlyList<SpeechModelOption> Options { get; } =
    [
        new(SpeechModelId.LargeV3Turbo, "Best accuracy (large, 1.6 GB)", LargeV3Turbo.SizeBytes),
        new(SpeechModelId.SmallEn, "Faster (small, 488 MB)", SmallEn.SizeBytes),
    ];

    public static SpeechModelInfo Get(SpeechModelId id) => id switch
    {
        SpeechModelId.LargeV3Turbo => LargeV3Turbo,
        SpeechModelId.SmallEn => SmallEn,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown speech model."),
    };

    private static Uri PinnedUrl(string fileName) =>
        new($"https://huggingface.co/ggerganov/whisper.cpp/resolve/{PinnedCommit}/{fileName}");
}
