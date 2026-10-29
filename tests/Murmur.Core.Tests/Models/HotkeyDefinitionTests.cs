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
    public void IsValid(int vk, HotkeyModifiers mods, bool expected) =>
        new HotkeyDefinition(vk, mods, "x").IsValid.ShouldBe(expected);
}
