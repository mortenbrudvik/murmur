using Autofac;
using Mediator;
using Murmur.Core.Features.App;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Interop;
using Murmur.WebView;
using NSubstitute;
using Shouldly;

namespace Murmur.UI.Tests;

/// <summary>
/// The generated mediator, reached through the real container: the source generator must have found the handlers in
/// Murmur.Core and Murmur.Interop, and Autofac must build them with their dependencies.
/// </summary>
public sealed class MediatorWiringTests
{
    // ApiBridge sends the request it deserialised as an object.
    [Fact]
    public async Task Bootstrap_SendObject_ReachesTheHandlerInCore()
    {
        using var container = AutofacBootstrapper.Bootstrap();
        var mediator = container.Resolve<IMediator>();

        var answer = await mediator.Send((object)new GetInputStatusRequest(), TestContext.Current.CancellationToken);

        answer.ShouldBeSameAs(container.Resolve<InputStatus>());
    }

    // The About window sends a typed request.
    [Fact]
    public async Task Bootstrap_SendTyped_ReachesTheHandlerInCore()
    {
        using var container = AutofacBootstrapper.Bootstrap();
        var mediator = container.Resolve<IMediator>();

        var answer = await mediator.Send(new GetInputStatusRequest(), TestContext.Current.CancellationToken);

        answer.ShouldBeSameAs(container.Resolve<InputStatus>());
    }

    // The handler lives in Murmur.Interop and is resolved from the scope the mediator came from.
    [Fact]
    public async Task Bootstrap_Publish_ReachesTheHandlerInInterop()
    {
        using var container = AutofacBootstrapper.Bootstrap();
        var events = Substitute.For<ApiEventAggregator>(Substitute.For<IWebViewInterop>());
        await using var scope = container.BeginLifetimeScope(b => b.RegisterInstance(events).As<ApiEventAggregator>());

        await scope.Resolve<IMediator>().Publish(new ShowSettingsNotification(), TestContext.Current.CancellationToken);

        events.Received(1).Publish("app.showSettings", Arg.Any<object>());
    }

    // Callers fire and forget notifications; a handler that throws must fault the task they forget (which logs it), not
    // throw into the caller: App raises the engine's StatusChanged, whose later subscribers would otherwise be skipped.
    [Fact]
    public async Task Bootstrap_PublishToAThrowingHandler_FaultsTheTaskInsteadOfThrowing()
    {
        using var container = AutofacBootstrapper.Bootstrap();
        var events = Substitute.For<ApiEventAggregator>(Substitute.For<IWebViewInterop>());
        events.When(e => e.Publish(Arg.Any<string>(), Arg.Any<object>())).Throw(new InvalidOperationException("no window"));
        await using var scope = container.BeginLifetimeScope(b => b.RegisterInstance(events).As<ApiEventAggregator>());
        var mediator = scope.Resolve<IMediator>();

        var publishing = Should.NotThrow(() => mediator.Publish(new ShowSettingsNotification(), TestContext.Current.CancellationToken));

        (await Should.ThrowAsync<InvalidOperationException>(publishing.AsTask())).Message.ShouldBe("no window");
    }

    // The same for requests: a handler that throws before its first await (an unknown link) faults the task, as callers
    // that await it expect, instead of throwing out of Send.
    [Fact]
    public async Task Bootstrap_SendToAHandlerThatThrowsAtOnce_FaultsTheTaskInsteadOfThrowing()
    {
        using var container = AutofacBootstrapper.Bootstrap();
        var mediator = container.Resolve<IMediator>();

        var sending = Should.NotThrow(() => mediator.Send(new OpenLinkRequest((LinkTarget)99), TestContext.Current.CancellationToken));

        await Should.ThrowAsync<ArgumentOutOfRangeException>(sending.AsTask());
    }

    // One handler instance per call, as it has always been here, so two IPC calls at once never share one: the mediator
    // asks the container on every Send (with Mediator's singleton default it would build the handler once and keep it).
    [Fact]
    public async Task Bootstrap_EverySendGetsItsOwnHandler()
    {
        using var container = AutofacBootstrapper.Bootstrap();
        var built = 0;
        await using var scope = container.BeginLifetimeScope(b => b
            .Register(c =>
            {
                built++;
                return new GetInputStatusHandler(c.Resolve<InputStatus>());
            })
            .As<IRequestHandler<GetInputStatusRequest, InputStatus>>());
        var mediator = scope.Resolve<IMediator>();

        await mediator.Send(new GetInputStatusRequest(), TestContext.Current.CancellationToken);
        await mediator.Send(new GetInputStatusRequest(), TestContext.Current.CancellationToken);

        built.ShouldBe(2);
    }

    // What app.ready, app.openLink, app.quit and the other requests that return nothing answer, through the real bridge
    // and mediator.
    [Fact]
    public async Task Bootstrap_VoidRequestThroughTheBridge_AnswersAnEmptyObject()
    {
        using var container = AutofacBootstrapper.Bootstrap();
        var registry = new ApiRegistry();
        registry.Register<UiReadyRequest>("app", "ready");
        var bridge = new ApiBridge(container.Resolve<IMediator>(), registry);

        var answer = await bridge.InvokeMethod("app", "ready", "{}");

        answer.ShouldBe("""{"success":true,"data":{}}""");
    }

    // ...and the container registers handlers per dependency, so resolving one directly also gives a new instance.
    [Fact]
    public void Bootstrap_ResolvesRequestHandlersPerCall()
    {
        using var container = AutofacBootstrapper.Bootstrap();

        container.Resolve<IRequestHandler<GetInputStatusRequest, InputStatus>>()
            .ShouldNotBeSameAs(container.Resolve<IRequestHandler<GetInputStatusRequest, InputStatus>>());
    }
}
