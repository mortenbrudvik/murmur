using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Murmur.UI.Splash;
using Shouldly;

namespace Murmur.UI.Tests.Splash;

/// <summary>
/// The splash must never be something the user has to sit through: a click or any key asks for it to be skipped. The
/// window is never shown here; its handle is enough to raise input on it.
/// </summary>
public sealed class SplashWindowTests
{
    /// <summary>With Windows' animation effects off, the busy dots must stand still, as the pill's do.</summary>
    [Fact]
    public void BusyDots_FollowWindowsAnimationSetting()
    {
        StaHost.Run(() =>
        {
            var window = new SplashWindow(new SplashViewModel("1.1.0"));

            var dots = window.FindName("BusyDots").ShouldBeOfType<Control>();

            dots.Tag.ShouldBe(SystemParameters.ClientAreaAnimation);
            window.Close();
        });
    }

    [Fact]
    public void MouseDown_AsksToBeSkipped()
    {
        StaHost.Run(() =>
        {
            var window = new SplashWindow(new SplashViewModel("1.1.0"));
            var skips = 0;
            window.SkipRequested += () => skips++;

            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });

            skips.ShouldBe(1);
            window.Close();
        });
    }

    [Fact]
    public void KeyDown_AnyKey_AsksToBeSkipped()
    {
        StaHost.Run(() =>
        {
            var window = new SplashWindow(new SplashViewModel("1.1.0"));
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
            var skips = 0;
            window.SkipRequested += () => skips++;

            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });

            skips.ShouldBe(1);
            window.Close();
        });
    }

    [Fact]
    public void NewOne_ShowsItsFirstFrameUntilPlayed()
    {
        StaHost.Run(() =>
        {
            var window = new SplashWindow(new SplashViewModel("1.1.0"));

            // At rest: the clock has not started. With animations turned off in Windows the canvas shows the final
            // frame instead, whatever the clock says.
            var expected = System.Windows.SystemParameters.ClientAreaAnimation ? 0 : SplashTimeline.End;
            window.CanvasTime.ShouldBe(expected);
            window.Close();
        });
    }

    [Fact]
    public void FadeOutAndCloseAsync_CalledTwice_IsOneClose()
    {
        StaHost.Run(() =>
        {
            var window = new SplashWindow(new SplashViewModel("1.1.0"));
            new WindowInteropHelper(window).EnsureHandle();
            var closed = 0;
            window.Closed += (_, _) => closed++;

            var first = window.FadeOutAndCloseAsync();
            var second = window.FadeOutAndCloseAsync();

            second.ShouldBeSameAs(first);
            StaHost.PumpUntil(() => first.IsCompleted, TimeSpan.FromSeconds(3)).ShouldBeTrue();
            closed.ShouldBe(1);
        });
    }
}
