using System.Globalization;
using System.Text;
using Murmur.Core.Models;

namespace Murmur.Core.Input;

/// <summary>Human-readable names for virtual-key codes, as shown in Settings and stored in <see cref="HotkeyDefinition.DisplayName"/>.</summary>
public static class KeyNames
{
    private static readonly Dictionary<int, string> Named = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0C] = "Clear",
        [0x0D] = "Enter",
        [0x10] = "Shift",
        [0x11] = "Ctrl",
        [0x12] = "Alt",
        [0x13] = "Pause",
        [0x14] = "Caps Lock",
        [0x1B] = "Esc",
        [0x20] = "Space",
        [0x21] = "Page Up",
        [0x22] = "Page Down",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2C] = "Print Screen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x5B] = "Left Win",
        [0x5C] = "Right Win",
        [0x5D] = "Menu",
        [0x6A] = "Num *",
        [0x6B] = "Num +",
        [0x6C] = "Num Separator",
        [0x6D] = "Num -",
        [0x6E] = "Num .",
        [0x6F] = "Num /",
        [0x90] = "Num Lock",
        [0x91] = "Scroll Lock",
        [0xA0] = "Left Shift",
        [0xA1] = "Right Shift",
        [0xA2] = "Left Ctrl",
        [0xA3] = "Right Ctrl",
        [0xA4] = "Left Alt",
        [0xA5] = "Right Alt",
        [0xAD] = "Volume Mute",
        [0xAE] = "Volume Down",
        [0xAF] = "Volume Up",
        [0xB0] = "Next Track",
        [0xB1] = "Previous Track",
        [0xB2] = "Stop Media",
        [0xB3] = "Play/Pause",
        [0xBA] = ";",
        [0xBB] = "=",
        [0xBC] = ",",
        [0xBD] = "-",
        [0xBE] = ".",
        [0xBF] = "/",
        [0xC0] = "`",
        [0xDB] = "[",
        [0xDC] = "\\",
        [0xDD] = "]",
        [0xDE] = "'",
        [0xE2] = "<",
    };

    public static string For(int vk)
    {
        if (Named.TryGetValue(vk, out var name))
            return name;

        return vk switch
        {
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => "Num " + (vk - 0x60).ToString(CultureInfo.InvariantCulture),
            >= 0x70 and <= 0x87 => "F" + (vk - 0x70 + 1).ToString(CultureInfo.InvariantCulture),
            _ => "Key 0x" + vk.ToString("X2", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The modifiers (in the order Ctrl, Alt, Shift, Win) followed by the key, joined with '+'.</summary>
    public static string Describe(int vk, HotkeyModifiers mods)
    {
        var text = new StringBuilder();

        if (mods.HasFlag(HotkeyModifiers.Ctrl))
            text.Append("Ctrl+");
        if (mods.HasFlag(HotkeyModifiers.Alt))
            text.Append("Alt+");
        if (mods.HasFlag(HotkeyModifiers.Shift))
            text.Append("Shift+");
        if (mods.HasFlag(HotkeyModifiers.Win))
            text.Append("Win+");

        return text.Append(For(vk)).ToString();
    }
}
