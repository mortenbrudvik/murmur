using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using Murmur.UI.Tray;
using Shouldly;

namespace Murmur.UI.Tests.Tray;

public sealed class TrayMenuPlacementTests
{
    /// <summary>A menu the way a theme draws one: a painted border set in from the popup's edge to leave room for a shadow.</summary>
    private const string ShadowedTemplate = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu">
          <Border Margin="24,20,28,32" Background="Black">
            <ItemsPresenter />
          </Border>
        </ControlTemplate>
        """;

    /// <summary>The same menu caught in the middle of a theme's opening animation, which slides it in from above.</summary>
    private const string SlidingTemplate = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu">
          <Border Margin="24,20,28,32" Background="Black">
            <Border.RenderTransform>
              <TranslateTransform Y="-90" />
            </Border.RenderTransform>
            <ItemsPresenter />
          </Border>
        </ControlTemplate>
        """;

    private const string UnpaintedTemplate = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu">
          <ItemsPresenter />
        </ControlTemplate>
        """;

    /// <summary>A 4K monitor, and a menu window of 300 by 250 whose painted part is set in by a shadow margin.</summary>
    private static readonly Rect Monitor = new(0, 0, 3840, 2160);
    private static readonly Rect Visible = new(36, 30, 222, 172);

    /// <summary>Like Windows' own tray menus: the menu opens upwards from the click, with its corner on it.</summary>
    [Fact]
    public void WindowOrigin_PutsTheVisibleMenusBottomLeftCornerOnTheAnchor()
    {
        var origin = TrayMenuPlacement.WindowOrigin(new Point(3000, 2000), Visible, Monitor);

        origin.ShouldBe(new Point(3000 - 36, 2000 - 202));
    }

    [Fact]
    public void WindowOrigin_NoRoomToTheRight_KeepsTheVisibleMenuOnTheMonitor()
    {
        var origin = TrayMenuPlacement.WindowOrigin(new Point(3800, 2000), Visible, Monitor);

        // The painted part ends at the monitor's edge; the shadow margin may hang over it.
        (origin.X + Visible.Right).ShouldBe(3840);
        origin.Y.ShouldBe(2000 - 202);
    }

    /// <summary>A taskbar at the top of the screen: nothing above the click, so the menu opens downwards from it.</summary>
    [Fact]
    public void WindowOrigin_NoRoomAbove_OpensDownwardsFromTheAnchor()
    {
        var origin = TrayMenuPlacement.WindowOrigin(new Point(3000, 40), Visible, Monitor);

        origin.ShouldBe(new Point(3000 - 36, 40 - 30));
    }

    /// <summary>A monitor to the left of the primary one has negative coordinates.</summary>
    [Fact]
    public void WindowOrigin_OnAMonitorLeftOfThePrimary_StaysOnThatMonitor()
    {
        var left = new Rect(-2560, 365, 2560, 1440);

        var origin = TrayMenuPlacement.WindowOrigin(new Point(-100, 1780), Visible, left);

        (origin.X + Visible.Right).ShouldBe(0);
        (origin.Y + Visible.Bottom).ShouldBe(1780);
    }

    [Fact]
    public void VisibleBounds_MenuDrawnInsideAShadowMargin_IsThePaintedPart()
    {
        StaHost.Run(() =>
        {
            var menu = Arranged(ShadowedTemplate);

            var visible = TrayMenuPlacement.VisibleBounds(menu);

            visible.Left.ShouldBe(24, 0.01);
            visible.Top.ShouldBe(20, 0.01);
            visible.Width.ShouldBe(menu.ActualWidth - 24 - 28, 0.01);
            visible.Height.ShouldBe(menu.ActualHeight - 20 - 32, 0.01);
            visible.Height.ShouldBeGreaterThan(10);
        });
    }

    /// <summary>
    /// The menu is placed while it is still sliding in. Where it is drawn at that moment is not where it will be:
    /// measured on the owner's PC, the menu was put 90 units too low and hung off the screen.
    /// </summary>
    [Fact]
    public void VisibleBounds_MenuStillSlidingIn_IsWhereItWillComeToRest()
    {
        StaHost.Run(() =>
        {
            var menu = Arranged(SlidingTemplate);

            var visible = TrayMenuPlacement.VisibleBounds(menu);

            visible.Top.ShouldBe(20, 0.01);
            visible.Bottom.ShouldBe(menu.ActualHeight - 32, 0.01);
        });
    }

    [Fact]
    public void VisibleBounds_NothingPainted_IsTheWholeMenu()
    {
        StaHost.Run(() =>
        {
            var menu = Arranged(UnpaintedTemplate);

            TrayMenuPlacement.VisibleBounds(menu).ShouldBe(new Rect(0, 0, menu.ActualWidth, menu.ActualHeight));
        });
    }

    /// <summary>A menu that has not been laid out yet has nothing to measure.</summary>
    [Fact]
    public void VisibleBounds_MenuNotLaidOut_IsEmpty()
    {
        StaHost.Run(() =>
        {
            TrayMenuPlacement.VisibleBounds(new ContextMenu()).ShouldBe(Rect.Empty);
        });
    }

    [Fact]
    public void TryPlace_MenuNotOpen_DoesNothing()
    {
        StaHost.Run(() =>
        {
            TrayMenuPlacement.TryPlace(Arranged(ShadowedTemplate), new Point(600, 500)).ShouldBeFalse();
        });
    }

    /// <summary>
    /// Opens a real menu for a moment, somewhere else, and moves it: afterwards its window is where the painted
    /// menu's bottom-left corner is on the anchor, in the screen's own pixels, whatever WPF made of the position.
    /// </summary>
    [Fact]
    public void TryPlace_OpenMenu_MovesItsWindowSoTheVisibleCornerIsOnTheAnchor()
    {
        StaHost.Run(() =>
        {
            var menu = new ContextMenu
            {
                Template = (ControlTemplate)XamlReader.Parse(ShadowedTemplate),
                Items = { new MenuItem { Header = "Settings" }, new MenuItem { Header = "About" }, new MenuItem { Header = "Quit" } },
                Placement = PlacementMode.AbsolutePoint,
                HorizontalOffset = 200,
                VerticalOffset = 200,
            };
            menu.IsOpen = true;
            try
            {
                var anchor = new Point(700, 600);

                TrayMenuPlacement.TryPlace(menu, anchor).ShouldBeTrue();

                var source = PresentationSource.FromVisual(menu).ShouldBeOfType<HwndSource>();
                var scale = source.CompositionTarget.TransformToDevice.M11;
                var window = TrayMenuNative.WindowBounds(source.Handle).ShouldNotBeNull();
                (window.Left + 24 * scale).ShouldBe(anchor.X, 1.5);
                (window.Top + (menu.ActualHeight - 32) * scale).ShouldBe(anchor.Y, 1.5);
            }
            finally
            {
                menu.IsOpen = false;
            }
        });
    }

    private static ContextMenu Arranged(string template)
    {
        var menu = new ContextMenu
        {
            Template = (ControlTemplate)XamlReader.Parse(template),
            Items = { new MenuItem { Header = "Settings" }, new MenuItem { Header = "About" }, new MenuItem { Header = "Quit" } },
        };
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize));
        return menu;
    }
}
