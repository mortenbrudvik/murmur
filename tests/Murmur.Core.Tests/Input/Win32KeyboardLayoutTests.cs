using System.Runtime.InteropServices;
using Murmur.Core.Input;
using Shouldly;

namespace Murmur.Core.Tests.Input;

public sealed class Win32KeyboardLayoutTests
{
    /// <summary>Norwegian (Bokmål): language 0x0414, layout 0x0414.</summary>
    private const uint NorwegianLayout = 0x04140414;

    private static readonly int[] OemKeys = [0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE, 0xDF, 0xE2];

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] nint[]? lpList);

    private static nint? InstalledLayout(uint handle)
    {
        var layouts = new nint[GetKeyboardLayoutList(0, null)];
        var count = GetKeyboardLayoutList(layouts.Length, layouts);
        foreach (var layout in layouts.Take(count))
        {
            if ((uint)layout == handle)
                return layout;
        }

        return null;
    }

    [Fact]
    public void ToCharacter_Zero_IsNull()
    {
        Win32KeyboardLayout.ToCharacter(0).ShouldBeNull();
    }

    [Fact]
    public void ToCharacter_PlainCharacter_IsThatCharacter()
    {
        Win32KeyboardLayout.ToCharacter('Ø').ShouldBe('Ø');
    }

    [Fact]
    public void ToCharacter_DeadKey_MasksTheDeadKeyBit()
    {
        // MapVirtualKeyEx sets the top bit of the result for a dead key, which leaves the accent itself in the low word.
        Win32KeyboardLayout.ToCharacter(0x80000000 | '¨').ShouldBe('¨');
    }

    [Fact]
    public void ToCharacter_OnlyTheDeadKeyBit_IsNull()
    {
        Win32KeyboardLayout.ToCharacter(0x80000000).ShouldBeNull();
    }

    [Fact]
    public void CharacterFor_NoLayoutHandle_IsNull()
    {
        new Win32KeyboardLayout(() => 0).CharacterFor(0xC0).ShouldBeNull();
    }

    [Fact]
    public void CharacterFor_EveryOemKeyOnTheForegroundLayout_IsNullOrAPrintableCharacter()
    {
        var layout = new Win32KeyboardLayout();

        foreach (var vk in OemKeys)
        {
            if (layout.CharacterFor(vk) is { } character)
            {
                char.IsControl(character).ShouldBeFalse($"0x{vk:X2}");
                char.IsWhiteSpace(character).ShouldBeFalse($"0x{vk:X2}");
            }
        }
    }

    [Fact]
    public void CharacterFor_NorwegianLayoutInstalled_NamesTheNorwegianKeys()
    {
        // Never loads a layout (that would change the user's language list): runs only where Norwegian is already there.
        var hkl = InstalledLayout(NorwegianLayout);
        Assert.SkipWhen(hkl is null, "the Norwegian keyboard layout is not installed");
        var layout = new Win32KeyboardLayout(() => hkl!.Value);

        char.ToUpperInvariant(layout.CharacterFor(0xC0)!.Value).ShouldBe('Ø');
        char.ToUpperInvariant(layout.CharacterFor(0xDD)!.Value).ShouldBe('Å');
        char.ToUpperInvariant(layout.CharacterFor(0xDE)!.Value).ShouldBe('Æ');
        layout.CharacterFor(0xBA).ShouldBe('¨');
        layout.CharacterFor(0xBF).ShouldBe('\'');
    }
}
