using System.Globalization;
using Murmur.Core.Models;
using Murmur.Core.Speech;

namespace Murmur.Core.Services;

/// <summary>The About window's wording: what is running, how much was dictated, and the lines "Copy details" copies.</summary>
public static class AboutText
{
    public const string Tagline = "Hold a key, talk, and the words land where you were typing. Nothing you say leaves this PC.";

    private const string NoModel = "Not chosen yet";

    /// <summary>The UI is English, so month names are too, whatever the user's culture; only digit grouping follows it.</summary>
    private static readonly CultureInfo MonthCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>The "Speech model" row: the model's name, once the engine has one.</summary>
    public static string SpeechModel(EngineStatus? status) =>
        string.IsNullOrEmpty(status?.ModelName) ? NoModel : status.ModelName;

    /// <summary>The "Running on" row: "NVIDIA GeForce RTX 3090 · CUDA", "Vulkan", "CPU", "Starting…" or "Not running".</summary>
    public static string RunningOn(EngineStatus? status) => Running(status) switch
    {
        { State: RunState.Starting } => "Starting…",
        { State: RunState.Stopped } => "Not running",
        { GpuName: { } gpu } running => $"{gpu} · {running.Runtime}",
        var running => running.Runtime!,
    };

    /// <summary>The "Dictated so far" row: the words, and below them the dictations and the month the history starts in.</summary>
    public static (string Main, string? Detail) Dictated(HistoryTotals totals, CultureInfo culture)
    {
        if (totals.Dictations == 0 || totals.Since is not { } since)
            return ("Nothing yet", null);

        var month = since.ToLocalTime().ToString("MMMM yyyy", MonthCulture);
        return (Plural(totals.Words, "word", culture), $"in {Plural(totals.Dictations, "dictation", culture)} since {month}");
    }

    /// <summary>What "Copy details" puts on the clipboard: four lines for a bug report.</summary>
    public static string Details(IAppInfo app, EngineStatus? status)
    {
        var runtime = Running(status) switch
        {
            { State: RunState.Starting } => "Starting",
            { State: RunState.Stopped } => "Not running",
            { GpuName: { } gpu } running => $"{running.Runtime}, {gpu}",
            var running => running.Runtime!,
        };

        return string.Join(
            "\n",
            $"Murmur {app.Version} ({app.Channel})",
            $"Model: {SpeechModel(status)}",
            $"Runtime: {runtime}",
            app.WindowsBuild);
    }

    /// <summary>
    /// What the engine runs on, as far as it can be vouched for. The status's <see cref="EngineStatus.Gpu"/> is the
    /// CUDA tip, not the device in use: it names a card whenever a CUDA-capable NVIDIA card was found, also while
    /// Vulkan does the work on some other card. So the card is named only when the runtime is CUDA.
    /// </summary>
    private static RunningOnWhat Running(EngineStatus? status)
    {
        if (status is null)
            return new RunningOnWhat(RunState.Starting);
        if (status.State == EngineState.Error)
            return new RunningOnWhat(RunState.Stopped);
        if (status.State != EngineState.Ready || string.IsNullOrEmpty(status.Runtime))
            return new RunningOnWhat(RunState.Starting);

        return new RunningOnWhat(
            RunState.Ready, EngineStatusText.RuntimeLabel(status.Runtime), status.Runtime == "Cuda" ? status.Gpu?.GpuName : null);
    }

    private static string Plural(long count, string noun, CultureInfo culture) =>
        $"{count.ToString("N0", culture)} {noun}{(count == 1 ? "" : "s")}";

    private enum RunState
    {
        Starting,
        Stopped,
        Ready
    }

    private readonly record struct RunningOnWhat(RunState State, string? Runtime = null, string? GpuName = null);
}
