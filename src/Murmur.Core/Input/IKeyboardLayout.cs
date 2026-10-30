namespace Murmur.Core.Input;

/// <summary>Asks the active keyboard layout what a key types, so a hotkey is named after the key the user sees.</summary>
public interface IKeyboardLayout
{
    /// <summary>
    /// The character <paramref name="virtualKey"/> types unshifted on the current layout, or null when the layout has
    /// none. A dead key (an accent such as the umlaut) answers with the accent itself.
    /// </summary>
    char? CharacterFor(int virtualKey);
}
