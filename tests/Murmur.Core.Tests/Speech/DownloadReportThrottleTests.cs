using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Models;
using Murmur.Core.Speech;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

public sealed class DownloadReportThrottleTests
{
    private const long Total = 1_000_000;

    private readonly FakeTimeProvider _time = new();

    private static DownloadProgress At(long received, int retries = 0) =>
        new("Whisper small (English)", received, Total, 1000, 10, retries);

    [Fact]
    public void ShouldReport_FirstReport_IsTrue()
    {
        new DownloadReportThrottle(_time).ShouldReport(At(0)).ShouldBeTrue();
    }

    [Fact]
    public void ShouldReport_SamePercentWithinASecond_IsFalse()
    {
        var throttle = new DownloadReportThrottle(_time);
        throttle.ShouldReport(At(420_000));

        _time.Advance(TimeSpan.FromMilliseconds(999));

        throttle.ShouldReport(At(429_999)).ShouldBeFalse();
    }

    [Fact]
    public void ShouldReport_PercentChanged_IsTrue()
    {
        var throttle = new DownloadReportThrottle(_time);
        throttle.ShouldReport(At(429_999));

        throttle.ShouldReport(At(430_000)).ShouldBeTrue();
        throttle.ShouldReport(At(430_001)).ShouldBeFalse();
    }

    [Fact]
    public void ShouldReport_RetriesChanged_IsTrue()
    {
        var throttle = new DownloadReportThrottle(_time);
        throttle.ShouldReport(At(420_000));

        throttle.ShouldReport(At(420_000, retries: 1)).ShouldBeTrue();
        throttle.ShouldReport(At(420_000, retries: 1)).ShouldBeFalse();
        throttle.ShouldReport(At(420_000, retries: 0)).ShouldBeTrue();
    }

    [Fact]
    public void ShouldReport_OneSecondLater_IsTrue()
    {
        var throttle = new DownloadReportThrottle(_time);
        throttle.ShouldReport(At(420_000));

        _time.Advance(TimeSpan.FromSeconds(1));

        throttle.ShouldReport(At(420_000)).ShouldBeTrue();
        throttle.ShouldReport(At(420_000)).ShouldBeFalse();
    }

    [Fact]
    public void ShouldReport_UnknownTotal_CountsAsZeroPercent()
    {
        var throttle = new DownloadReportThrottle(_time);
        throttle.ShouldReport(new DownloadProgress("m", 10, 0, 0, null, 0));

        throttle.ShouldReport(new DownloadProgress("m", 5000, 0, 0, null, 0)).ShouldBeFalse();
    }
}
