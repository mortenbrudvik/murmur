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
        KeyNames.For(vk, FakeKeyboardLayout.Unmapped).ShouldBe(expected);
    }

    [Fact]
    public void For_UnknownKey_FallsBackToHex()
    {
        KeyNames.For(0xFF, FakeKeyboardLayout.Unmapped).ShouldBe("Key 0xFF");
    }

    [Fact]
    public void Describe_CtrlShiftSpace()
    {
        KeyNames.Describe(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, FakeKeyboardLayout.Unmapped).ShouldBe("Ctrl+Shift+Space");
    }

    [Fact]
    public void Describe_AllModifiers_InFixedOrder()
    {
        KeyNames.Describe(0x41, HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Ctrl, FakeKeyboardLayout.Unmapped)
            .ShouldBe("Ctrl+Alt+Shift+Win+A");
    }

    [Fact]
    public void Describe_NoModifiers_IsKeyName()
    {
        KeyNames.Describe(0xA3, HotkeyModifiers.None, FakeKeyboardLayout.Unmapped).ShouldBe(HotkeyDefinition.Default.DisplayName);
    }

    [Theory]
    [InlineData(0xC0, "Ø")]
    [InlineData(0xDD, "Å")]
    [InlineData(0xDE, "Æ")]
    [InlineData(0xBB, "+")]
    [InlineData(0xBF, "'")]
    [InlineData(0xDB, "\\")]
    [InlineData(0xDC, "|")]
    public void For_OemKeyOnNordicLayout_UsesTheLayoutsCharacter(int vk, string expected)
    {
        KeyNames.For(vk, FakeKeyboardLayout.Nordic).ShouldBe(expected);
    }

    [Fact]
    public void For_DeadKeyOnNordicLayout_ShowsTheDeadKeysCharacter()
    {
        KeyNames.For(0xBA, FakeKeyboardLayout.Nordic).ShouldBe("¨");
    }

    [Fact]
    public void Describe_CtrlWithNordicOemKey_UsesTheLayoutsCharacter()
    {
        KeyNames.Describe(0xDD, HotkeyModifiers.Ctrl, FakeKeyboardLayout.Nordic).ShouldBe("Ctrl+Å");
    }

    [Theory]
    [InlineData(0xBA, ";")]
    [InlineData(0xBB, "=")]
    [InlineData(0xBF, "/")]
    [InlineData(0xC0, "`")]
    [InlineData(0xDB, "[")]
    [InlineData(0xDC, "\\")]
    [InlineData(0xDD, "]")]
    [InlineData(0xDE, "'")]
    [InlineData(0xE2, "<")]
    public void For_OemKeyTheLayoutCannotMap_FallsBackToUsName(int vk, string expected)
    {
        KeyNames.For(vk, FakeKeyboardLayout.Unmapped).ShouldBe(expected);
    }

    [Theory]
    [InlineData('\0')]
    [InlineData('\r')]
    [InlineData(' ')]
    public void For_OemKeyTheLayoutNamesWithAnUnprintableCharacter_FallsBackToUsName(char unprintable)
    {
        var layout = new FakeKeyboardLayout(new Dictionary<int, char> { [0xC0] = unprintable });

        KeyNames.For(0xC0, layout).ShouldBe("`");
    }

    [Fact]
    public void For_OemKeyWithNoUsName_ThatTheLayoutKnows_UsesTheLayoutsCharacter()
    {
        var layout = new FakeKeyboardLayout(new Dictionary<int, char> { [0xDF] = '§' });

        KeyNames.For(0xDF, layout).ShouldBe("§");
    }

    [Fact]
    public void For_OemKeyWithNoUsName_ThatTheLayoutCannotMap_FallsBackToHex()
    {
        KeyNames.For(0xDF, FakeKeyboardLayout.Unmapped).ShouldBe("Key 0xDF");
    }

    [Fact]
    public void For_NonOemKey_IgnoresTheLayout()
    {
        // A layout that names every key 'x' would rename these if it were consulted.
        var layout = new FakeKeyboardLayout(Enumerable.Range(0, 256).ToDictionary(vk => vk, _ => 'x'));

        KeyNames.For(0x41, layout).ShouldBe("A");
        KeyNames.For(0x20, layout).ShouldBe("Space");
        KeyNames.For(0xA3, layout).ShouldBe("Right Ctrl");
        layout.Asked.ShouldBeEmpty();
    }

    [Fact]
    public void For_OemKeys_AskTheLayoutOnlyAboutTheOemRange()
    {
        var layout = new FakeKeyboardLayout();

        for (var vk = 0; vk < 256; vk++)
            KeyNames.For(vk, layout);

        layout.Asked.ShouldBe([0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE, 0xDF, 0xE2]);
    }

    [Theory]
    [InlineData(0xBA, true)]
    [InlineData(0xC0, true)]
    [InlineData(0xDB, true)]
    [InlineData(0xDF, true)]
    [InlineData(0xE2, true)]
    [InlineData(0xB9, false)]
    [InlineData(0xC1, false)]
    [InlineData(0xDA, false)]
    [InlineData(0xE0, false)]
    [InlineData(0xE3, false)]
    [InlineData(0x41, false)]
    public void IsLayoutDependent_IsTrueForTheOemKeysOnly(int vk, bool expected)
    {
        KeyNames.IsLayoutDependent(vk).ShouldBe(expected);
    }
}
