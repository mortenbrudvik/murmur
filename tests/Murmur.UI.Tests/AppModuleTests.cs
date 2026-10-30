using Autofac;
using Murmur.Core.Services;
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
}
