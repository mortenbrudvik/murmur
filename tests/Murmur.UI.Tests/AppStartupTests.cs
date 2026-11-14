using Murmur.Core.Services;
using NSubstitute;
using Shouldly;

namespace Murmur.UI.Tests;

public sealed class AppStartupTests
{
    private static readonly IPackageIdentity Unpackaged = Substitute.For<IPackageIdentity>();

    [Fact]
    public void IsSignInStart_MinimizedByTheRunKey_True()
    {
        App.IsSignInStart(LaunchArguments.Parse(["--minimized"]), Unpackaged).ShouldBeTrue();
    }

    [Fact]
    public void IsSignInStart_StartedByHand_False()
    {
        App.IsSignInStart(LaunchArguments.Parse([]), Unpackaged).ShouldBeFalse();
    }

    /// <summary>The user asked for the restart and wants to see the window, on Settings.</summary>
    [Fact]
    public void IsSignInStart_RestartedByMurmur_False()
    {
        App.IsSignInStart(LaunchArguments.Parse(LaunchArguments.RestartArguments), Unpackaged).ShouldBeFalse();
        App.IsSignInStart(LaunchArguments.Parse(["--restarted", "--minimized"]), Unpackaged).ShouldBeFalse();
    }

    [Fact]
    public void ShouldShowSplash_StartedByHand_True()
    {
        App.ShouldShowSplash(LaunchArguments.Parse([]), Unpackaged).ShouldBeTrue();
    }

    [Fact]
    public void ShouldShowSplash_RestartedByMurmur_True()
    {
        App.ShouldShowSplash(LaunchArguments.Parse(LaunchArguments.RestartArguments), Unpackaged).ShouldBeTrue();
    }

    /// <summary>Murmur starting with Windows stays silent: no window, and no splash either.</summary>
    [Fact]
    public void ShouldShowSplash_SignInStart_False()
    {
        App.ShouldShowSplash(LaunchArguments.Parse(["--minimized"]), Unpackaged).ShouldBeFalse();
    }
}
