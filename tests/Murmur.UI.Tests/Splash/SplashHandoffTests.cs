using Murmur.UI.Splash;
using Shouldly;

namespace Murmur.UI.Tests.Splash;

/// <summary>
/// When the splash closes, the main window takes the focus only if Murmur still has it. Symptom if this regresses: a
/// user who went back to another app during the splash has the dictation they just started pasted into Murmur.
/// </summary>
public sealed class SplashHandoffTests
{
    [Fact]
    public void ShouldActivate_MurmurHasTheForeground_True()
    {
        SplashHandoff.ShouldActivate(foregroundProcessId: 42, ownProcessId: 42).ShouldBeTrue();
    }

    [Fact]
    public void ShouldActivate_AnotherAppHasTheForeground_False()
    {
        SplashHandoff.ShouldActivate(foregroundProcessId: 7, ownProcessId: 42).ShouldBeFalse();
    }

    [Fact]
    public void ShouldActivate_NoWindowHasTheForeground_False()
    {
        SplashHandoff.ShouldActivate(foregroundProcessId: 0, ownProcessId: 42).ShouldBeFalse();
    }
}
