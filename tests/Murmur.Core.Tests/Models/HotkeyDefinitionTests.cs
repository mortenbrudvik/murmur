using Murmur.Core.Models;
using Shouldly;

namespace Murmur.Core.Tests.Models;

public class HotkeyDefinitionTests
{
    [Fact]
    public void Default_IsRightCtrlHold()
    {
        var d = HotkeyDefinition.Default;

        d.VirtualKey.ShouldBe(0xA3);
        d.Modifiers.ShouldBe(HotkeyModifiers.None);
        d.DisplayName.ShouldBe("Right Ctrl");
        d.IsModifierOnly.ShouldBeTrue();
        d.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0x41, HotkeyModifiers.None, false)]                          // A alone
    [InlineData(0x20, HotkeyModifiers.None, false)]                          // Space alone
    [InlineData(0x41, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, true)]
    [InlineData(0x7C, HotkeyModifiers.None, true)]                           // F13
    [InlineData(0xA5, HotkeyModifiers.None, true)]                           // Right Alt
    [InlineData(0x09, HotkeyModifiers.Alt, false)]                           // Alt+Tab
    [InlineData(0x09, HotkeyModifiers.Alt | HotkeyModifiers.Shift, false)]   // Alt+Shift+Tab
    [InlineData(0x09, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, false)]    // Ctrl+Alt+Tab
    [InlineData(0x09, HotkeyModifiers.Win, false)]                           // Win+Tab
    [InlineData(0x73, HotkeyModifiers.Alt, false)]                           // Alt+F4
    [InlineData(0x09, HotkeyModifiers.Ctrl, true)]                           // Ctrl+Tab
    [InlineData(0x73, HotkeyModifiers.Ctrl, true)]                           // Ctrl+F4
    [InlineData(0x73, HotkeyModifiers.None, true)]                           // F4 alone
    [InlineData(0x20, HotkeyModifiers.Alt, true)]                            // Alt+Space
    public void IsValid(int vk, HotkeyModifiers mods, bool expected) =>
        new HotkeyDefinition(vk, mods, "x").IsValid.ShouldBe(expected);
}
