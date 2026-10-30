using Murmur.Core.Models;

namespace Murmur.Core.Speech;

/// <summary>
/// Picks the download progress reports worth passing on as an engine status. <see cref="ModelManager"/> reports every
/// write, about 100,000 times for the large model, and each status crosses to the web UI and the tray.
/// </summary>
/// <remarks>Not thread-safe: one instance per download, fed from the download's own progress callback.</remarks>
internal sealed class DownloadReportThrottle(TimeProvider time)
{
    /// <summary>How long the same percentage may stand before speed and time left are passed on again.</summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromSeconds(1);

    private bool _reported;
    private int _percent;
    private int _retries;
    private long _reportedAt;

    /// <summary>True for the first report, a new whole percent, a change in retries, or a second since the last one.</summary>
    public bool ShouldReport(DownloadProgress progress)
    {
        var percent = WholePercent(progress);
        var now = time.GetTimestamp();
        if (_reported && percent == _percent && progress.Retries == _retries && time.GetElapsedTime(_reportedAt, now) < Interval)
            return false;

        _reported = true;
        _percent = percent;
        _retries = progress.Retries;
        _reportedAt = now;
        return true;
    }

    private static int WholePercent(DownloadProgress progress) =>
        progress.TotalBytes > 0 ? (int)(Math.Clamp(progress.BytesReceived, 0, progress.TotalBytes) * 100 / progress.TotalBytes) : 0;
}
