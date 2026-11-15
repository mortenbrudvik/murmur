using Autofac;
using Mediator;
using Murmur.Core.Features.App;
using Murmur.Core.Input;
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

    // One handler instance per call, as it has always been here, so two IPC calls at once never share one.
    [Fact]
    public void Bootstrap_ResolvesRequestHandlersPerCall()
    {
        using var container = AutofacBootstrapper.Bootstrap();

        container.Resolve<IRequestHandler<GetInputStatusRequest, InputStatus>>()
            .ShouldNotBeSameAs(container.Resolve<IRequestHandler<GetInputStatusRequest, InputStatus>>());
    }
}
