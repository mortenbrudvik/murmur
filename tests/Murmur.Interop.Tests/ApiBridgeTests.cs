using MediatR;
using NSubstitute;
using Murmur.Interop;
using Shouldly;

namespace Murmur.Interop.Tests;

public class ApiBridgeTests
{
    [Fact]
    public async Task InvokeMethod_ReturnsError_WhenApiNotRegistered()
    {
        var mediator = Substitute.For<IMediator>();
        var registry = new ApiRegistry();
        var bridge = new ApiBridge(mediator, registry);

        var result = await bridge.InvokeMethod("unknown", "method", "{}");
        result.ShouldContain("Unknown API");
    }

    [Fact]
    public async Task InvokeMethod_DispatchesToMediatR_WhenRegistered()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<object?>("result"));

        var registry = new ApiRegistry();
        registry.Register<FakeRequest>("test", "get");

        var bridge = new ApiBridge(mediator, registry);
        var result = await bridge.InvokeMethod("test", "get", "{}");

        result.ShouldContain("\"success\":true");
        await mediator.Received(1).Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    private class FakeRequest : IRequest<string> { }
}
