using Autofac;
using MediatR;
using Murmur.Core.Features.App;
using Murmur.Core.Features.Models;
using Murmur.Core.Features.Setup;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Setup;
using NSubstitute;
using Shouldly;

namespace Murmur.UI.Tests;

public sealed class AppModuleTests
{
    /// <summary>
    /// The one decision package identity makes. Getting it backwards is silent in both directions: the Run key inside a
    /// package is written to a hive Explorer never reads, and the startup task outside one does not exist. Debug builds
    /// never register at all, packaged or not.
    /// </summary>
    [Theory]
    [InlineData(false, false, typeof(StartupRegistration))]
    [InlineData(false, true, typeof(PackagedStartupRegistration))]
    [InlineData(true, false, typeof(NoopStartupRegistration))]
    [InlineData(true, true, typeof(NoopStartupRegistration))]
    public void RegisterStartup_PicksTheImplementation_ForTheBuildAndPackageIdentity(bool debugBuild, bool packaged, Type expected)
    {
        var identity = Substitute.For<IPackageIdentity>();
        identity.IsPackaged.Returns(packaged);
        var builder = new ContainerBuilder();

        AppModule.RegisterStartup(builder, identity, debugBuild);

        using var container = builder.Build();
        container.Resolve<IStartupRegistration>().ShouldBeOfType(expected);
    }

    /// <summary>App asks the registered probe how Murmur was started; a second probe would be a second answer.</summary>
    [Fact]
    public void RegisterStartup_RegistersThePackageIdentityProbe()
    {
        var identity = Substitute.For<IPackageIdentity>();
        var builder = new ContainerBuilder();

        AppModule.RegisterStartup(builder, identity, debugBuild: false);

        using var container = builder.Build();
        container.Resolve<IPackageIdentity>().ShouldBeSameAs(identity);
    }

    /// <summary>
    /// The IPC requests the setup page sends are registered with the bridge by hand, but their handlers and what they
    /// need come from the container; one that cannot be built would only fail when the page first calls it.
    /// </summary>
    [Fact]
    public void Bootstrap_ResolvesTheSetupModelChoiceAndAppHandlers()
    {
        using var container = AutofacBootstrapper.Bootstrap();

        container.Resolve<IRequestHandler<GetSetupStateRequest, SetupState>>().ShouldNotBeNull();
        container.Resolve<IRequestHandler<UpdateSetupStateRequest, SetupState>>().ShouldNotBeNull();
        container.Resolve<IRequestHandler<GetModelChoiceRequest, ModelChoice>>().ShouldNotBeNull();
        container.Resolve<IRequestHandler<SetModelChoiceRequest, ModelChoice>>().ShouldNotBeNull();
        container.Resolve<IRequestHandler<OpenLinkRequest>>().ShouldNotBeNull();
        container.Resolve<IRequestHandler<RestartAppRequest>>().ShouldNotBeNull();
    }

    /// <summary>App listens to the lifecycle's event while the handler calls it through the interface: one instance.</summary>
    [Fact]
    public void Bootstrap_SharesOneInstanceOfTheSetupServices()
    {
        using var container = AutofacBootstrapper.Bootstrap();

        container.Resolve<IAppLifecycle>().ShouldBeSameAs(container.Resolve<AppLifecycle>());
        container.Resolve<ILinkOpener>().ShouldBeOfType<ShellLinkOpener>();
        container.Resolve<SetupCoordinator>().ShouldBeSameAs(container.Resolve<SetupCoordinator>());
        container.Resolve<LaunchOptions>().ShouldBeSameAs(container.Resolve<LaunchOptions>());
    }
}
