using NSubstitute;
using Murmur.Interop;
using Murmur.WebView;
using Shouldly;

namespace Murmur.Interop.Tests;

public class ApiEventAggregatorTests
{
    [Fact]
    public void Publish_DoesNotThrow_WhenApplicationCurrentIsNull()
    {
        var mockInterop = Substitute.For<IWebViewInterop>();
        var aggregator = new ApiEventAggregator(mockInterop);
        Should.NotThrow(() => aggregator.Publish("test.event", new { Value = 42 }));
    }
}
