using Murmur.Core.Hardware;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Serilog;

namespace Murmur.Core.Speech;

/// <summary>Recommends a speech model for this PC's GPU, and settles which model is the active one.</summary>
public sealed class ModelSelection(ISettingsService settings, IGpuInfo gpu, IModelManager models)
{
    /// <summary>
    /// The dedicated video memory from which the large model is recommended: 3.5 GiB. Large-v3-turbo wants about
    /// 4 GB, but a card sold as 4 GB reports a little under 4 GiB (4 293 918 720 bytes on one), so a 4 GiB threshold
    /// would put every such card on the small model.
    /// </summary>
    public const long UsableGpuMemory = 3_758_096_384;

    private readonly ILogger _logger = Log.ForContext<ModelSelection>();

    /// <summary>The model this PC's GPU suits, from <see cref="IGpuInfo.Report"/>.</summary>
    public SpeechModelId Recommended => Recommend(gpu.Report);

    /// <summary>
    /// The saved model, or, when settings have none yet, the model <see cref="Resolve"/> picks, which is then saved so
    /// the choice does not change from one start to the next.
    /// </summary>
    public SpeechModelId ResolveActive()
    {
        if (settings.Load().SpeechModel is { } saved)
            return saved;

        var recommended = Recommended;
        var largePresent = models.HasExpectedFile(SpeechModelCatalog.LargeV3Turbo);
        var resolved = Resolve(null, recommended, largePresent);

        // A choice saved since the Load above wins over this one.
        var updated = settings.Update(s => s with { SpeechModel = s.SpeechModel ?? resolved });
        var active = updated.SpeechModel ?? resolved;
        _logger.Information("Speech model set to {Model} (recommended {Recommended}, large model file present: {LargePresent})",
            active, recommended, largePresent);
        return active;
    }

    /// <summary>Large when any adapter has at least <see cref="UsableGpuMemory"/> of dedicated video memory, otherwise small.</summary>
    public static SpeechModelId Recommend(GpuReport report) =>
        report.Adapters.Any(a => a.DedicatedVideoMemory >= UsableGpuMemory) ? SpeechModelId.LargeV3Turbo : SpeechModelId.SmallEn;

    /// <summary>
    /// <paramref name="saved"/> when there is one; otherwise large when its file is already there (someone upgrading
    /// from 1.0 keeps the model they have), and <paramref name="recommended"/> in every other case.
    /// </summary>
    public static SpeechModelId Resolve(SpeechModelId? saved, SpeechModelId recommended, bool largePresent) =>
        saved ?? (largePresent ? SpeechModelId.LargeV3Turbo : recommended);
}
