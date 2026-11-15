using NSubstitute;
using Murmur.Interop;
using Murmur.WebView;
using Shouldly;

namespace Murmur.Interop.Tests;

public class ApiEventAggregatorTests
{
    [Fact]
    public void Publish_PostsTheEventAsCamelCaseJson()
    {
        var webView = Substitute.For<IWebViewInterop>();
        var aggregator = new ApiEventAggregator(webView);

        aggregator.Publish("test.event", new { Value = 42 });

        webView.Received(1).PostWebMessageAsJson("""{"eventName":"test.event","data":{"value":42}}""");
    }

    [Fact]
    public void Publish_FromAnotherThread_StillPosts()
    {
        var webView = Substitute.For<IWebViewInterop>();
        var aggregator = new ApiEventAggregator(webView);

        var thread = new Thread(() => aggregator.Publish("test.event", new { Value = 1 }));
        thread.Start();
        thread.Join();

        webView.ReceivedWithAnyArgs(1).PostWebMessageAsJson(default!);
    }
}
