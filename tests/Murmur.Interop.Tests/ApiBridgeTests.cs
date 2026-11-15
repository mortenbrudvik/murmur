using System.Text.Json;
using Mediator;
using Murmur.Core.Audio;
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
    public async Task InvokeMethod_DispatchesToMediator_WhenRegistered()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<object?>("result"));

        var registry = new ApiRegistry();
        registry.Register<FakeRequest>("test", "get");

        var bridge = new ApiBridge(mediator, registry);
        var result = await bridge.InvokeMethod("test", "get", "{}");

        result.ShouldContain("\"success\":true");
        await mediator.Received(1).Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeMethod_RegisteredRequest_ReturnsTheHandlersResultAsData()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<object?>("result"));
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", "{}");

        ShouldBeSuccess(result).GetString().ShouldBe("result");
    }

    // A handler that waits (CaptureHotkey waits up to 10 s): the bridge must answer with what it finally returns.
    [Fact]
    public async Task InvokeMethod_HandlerCompletesLater_ReturnsItsResultAsData()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<object?>(LaterAsync()));
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", "{}");

        ShouldBeSuccess(result).GetString().ShouldBe("later");

        static async Task<object?> LaterAsync()
        {
            await Task.Delay(20);
            return "later";
        }
    }

    // OpenLink, QuitApp, RestartApp, UiReady and StopMicTest answer nothing; the web app only reads success.
    [Fact]
    public async Task InvokeMethod_VoidRequest_ReturnsAnEmptyObjectAsData()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<object?>(Unit.Value));
        var registry = new ApiRegistry();
        registry.Register<FakeVoidRequest>("test", "do");
        var bridge = new ApiBridge(mediator, registry);

        var result = await bridge.InvokeMethod("test", "do", "{}");

        var data = ShouldBeSuccess(result);
        data.ValueKind.ShouldBe(JsonValueKind.Object);
        data.EnumerateObject().ShouldBeEmpty();
    }

    // A handler that throws before its first await throws out of Send itself instead of returning a faulted task.
    [Fact]
    public async Task InvokeMethod_SendThrowsSynchronously_ReturnsError()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<object?>>(_ => throw new ArgumentException("thrown at once"));
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", "{}");

        ShouldBeError(result).ShouldBe("thrown at once");
    }

    [Fact]
    public async Task InvokeMethod_HandlerRefuses_ReturnsItsMessage()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException<object?>(new ArgumentException("There can be at most 200 rules.")));
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", "{}");

        ShouldBeError(result).ShouldBe("There can be at most 200 rules.");
    }

    [Fact]
    public async Task InvokeMethod_HandlerHasABug_ReturnsTheGenericMessage()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException<object?>(new InvalidOperationException("Collection was modified")));
        var bridge = BridgeFor(mediator);

        var result = await bridge.InvokeMethod("test", "get", "{}");

        ShouldBeError(result).ShouldBe(ApiBridge.UnexpectedError);
    }

    [Theory]
    [MemberData(nameof(MessagesForPeople))]
    public void ErrorMessage_ExceptionsWrittenForPeople_PassTheirMessageOn(Exception ex)
    {
        ApiBridge.ErrorMessage(ex).ShouldBe(ex.Message);
    }

    public static TheoryData<Exception> MessagesForPeople() =>
    [
        new ArgumentException("There can be at most 200 rules."),
        new IOException("There is not enough space on the disk."),
        new FileNotFoundException("Could not find the file."),
        new UnauthorizedAccessException("Access is denied."),
        new AudioDeviceException("Microphone 'x' is not available (Unplugged)."),
    ];

    [Theory]
    [MemberData(nameof(Bugs))]
    public void ErrorMessage_OtherExceptions_AreGeneric(Exception ex)
    {
        ApiBridge.ErrorMessage(ex).ShouldBe(ApiBridge.UnexpectedError);
    }

    public static TheoryData<Exception> Bugs() =>
    [
        new NullReferenceException(),
        new InvalidOperationException("Sequence contains no elements"),
        new ArgumentNullException("value"),
        new System.Text.Json.JsonException("'n' is an invalid start of a value."),
    ];

    [Fact]
    public void ScriptSeesOnlyInvokeMethod()
    {
        typeof(ApiBridge).GetCustomAttributes(typeof(System.Runtime.InteropServices.ClassInterfaceAttribute), false)
            .ShouldHaveSingleItem().ShouldBeOfType<System.Runtime.InteropServices.ClassInterfaceAttribute>()
            .Value.ShouldBe(System.Runtime.InteropServices.ClassInterfaceType.None);
        typeof(IApiBridge).GetMethods().Select(m => m.Name).ShouldBe(["InvokeMethod"]);
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

    /// <summary>Asserts the bridge's success shape, <c>{"success":true,"data":...}</c>, and returns <c>data</c>.</summary>
    private static JsonElement ShouldBeSuccess(string response)
    {
        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        root.GetProperty("success").GetBoolean().ShouldBeTrue();
        return root.GetProperty("data").Clone();
    }

    private class FakeRequest : IRequest<string> { }

    private class FakeVoidRequest : IRequest { }
}
