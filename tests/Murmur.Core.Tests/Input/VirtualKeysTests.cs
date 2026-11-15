using Murmur.Core.Input;
using Shouldly;

namespace Murmur.Core.Tests.Input;

public sealed class VirtualKeysTests
{
    [Theory]
    [InlineData(0xA0, true)] // Left Shift
    [InlineData(0xA5, true)] // Right Alt
    [InlineData(0x5B, true)] // Left Win
    [InlineData(0x5C, true)] // Right Win
    [InlineData(0x10, false)] // generic Shift
    [InlineData(0x11, false)] // generic Ctrl
    [InlineData(0x12, false)] // generic Alt
    [InlineData(0x1B, false)] // Esc
    [InlineData(0xA6, false)] // Browser Back, just past Right Alt
    public void IsSideSpecificModifier_Key_TellsTheSideSpecificModifiers(int vk, bool expected)
    {
        VirtualKeys.IsSideSpecificModifier(vk).ShouldBe(expected);
    }

    [Fact]
    public void Modifiers_AreTheSideSpecificOnesThenTheGenericOnes()
    {
        var modifiers = VirtualKeys.Modifiers.ToArray();

        modifiers.ShouldBe([0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C, 0x10, 0x11, 0x12]);
        modifiers.Length.ShouldBeLessThanOrEqualTo(32); // one bit each in the tracker's mask
    }

    [Fact]
    public void EitherSideModifiers_AreTheGenericCodesAndBothWinKeys()
    {
        VirtualKeys.EitherSideModifiers.ToArray().ShouldBe([0x10, 0x11, 0x12, 0x5B, 0x5C]);
    }
}
