using System.Text.Json;
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

    [Fact]
    public async Task InvokeMethod_HandlerThrows_ReturnsErrorWithExceptionMessage()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<object?>(new InvalidOperationException("handler failed")));
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", "{}");

        ShouldBeError(result).ShouldBe("handler failed");
    }

    [Fact]
    public async Task InvokeMethod_MalformedJson_ReturnsErrorWithoutDispatching()
    {
        var mediator = Substitute.For<IMediator>();
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", "{not json");

        ShouldBeError(result).ShouldNotBeNullOrWhiteSpace();
        await mediator.DidNotReceive().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    // What WebViewApiClient sends for call(service, method, null): JSON.stringify(null).
    [Fact]
    public async Task InvokeMethod_NullJsonParams_ReturnsInvalidParamsWithoutDispatching()
    {
        var mediator = Substitute.For<IMediator>();
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", "null");

        ShouldBeError(result).ShouldBe("Invalid params for test.get");
        await mediator.DidNotReceive().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    // Only a caller other than the web app's client can pass a null string (it always sends JSON text).
    [Fact]
    public async Task InvokeMethod_NullString_ReturnsAnErrorInsteadOfThrowing()
    {
        var mediator = Substitute.For<IMediator>();
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", null!);

        ShouldBeError(result).ShouldNotBeNullOrWhiteSpace();
        await mediator.DidNotReceive().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    private static ApiBridge BridgeFor(IMediator mediator)
    {
        var registry = new ApiRegistry();
        registry.Register<FakeRequest>("test", "get");
        return new ApiBridge(mediator, registry);
    }

    /// <summary>Asserts the bridge's failure shape, <c>{"success":false,"error":"..."}</c>, and returns the error.</summary>
    private static string? ShouldBeError(string response)
    {
        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        root.GetProperty("success").GetBoolean().ShouldBeFalse();
        root.TryGetProperty("data", out _).ShouldBeFalse();
        return root.GetProperty("error").GetString();
    }

    private class FakeRequest : IRequest<string> { }
}
