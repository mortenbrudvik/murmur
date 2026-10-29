using Murmur.Core.Input;
using Murmur.Core.Models;
using Shouldly;

namespace Murmur.Core.Tests.Input;

public sealed class KeyNamesTests
{
    [Theory]
    [InlineData(0xA3, "Right Ctrl")]
    [InlineData(0xA2, "Left Ctrl")]
    [InlineData(0xA5, "Right Alt")]
    [InlineData(0xA0, "Left Shift")]
    [InlineData(0x5C, "Right Win")]
    [InlineData(0x20, "Space")]
    [InlineData(0x70, "F1")]
    [InlineData(0x7C, "F13")]
    [InlineData(0x87, "F24")]
    [InlineData(0x41, "A")]
    [InlineData(0x5A, "Z")]
    [InlineData(0x30, "0")]
    [InlineData(0x1B, "Esc")]
    [InlineData(0x0D, "Enter")]
    [InlineData(0x60, "Num 0")]
    public void For_KnownKeys(int vk, string expected)
    {
        KeyNames.For(vk).ShouldBe(expected);
    }

    [Fact]
    public void For_UnknownKey_FallsBackToHex()
    {
        KeyNames.For(0xFF).ShouldBe("Key 0xFF");
    }

    [Fact]
    public void Describe_CtrlShiftSpace()
    {
        KeyNames.Describe(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift).ShouldBe("Ctrl+Shift+Space");
    }

    [Fact]
    public void Describe_AllModifiers_InFixedOrder()
    {
        KeyNames.Describe(0x41, HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Ctrl)
            .ShouldBe("Ctrl+Alt+Shift+Win+A");
    }

    [Fact]
    public void Describe_NoModifiers_IsKeyName()
    {
        KeyNames.Describe(0xA3, HotkeyModifiers.None).ShouldBe(HotkeyDefinition.Default.DisplayName);
    }
}
