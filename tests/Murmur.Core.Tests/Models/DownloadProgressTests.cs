using Murmur.Common;
using Murmur.Core.Models;
using Shouldly;

namespace Murmur.Core.Tests.Models;

public sealed class DownloadProgressTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    public void Reconnecting_FollowsTheRetryCount(int retries, bool expected)
    {
        new DownloadProgress("model", 10, 100, 5, 18, retries).Reconnecting.ShouldBe(expected);
    }

    [Fact]
    public void Serialize_IncludesReconnectingForTheWeb()
    {
        var json = JsonUtils.Serialize(new DownloadProgress("model", 10, 100, 0, null, Retries: 2));

        json.ShouldContain("\"retries\":2");
        json.ShouldContain("\"reconnecting\":true");
    }
}
