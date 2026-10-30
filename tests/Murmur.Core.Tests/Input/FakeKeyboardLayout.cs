using Murmur.Core.Input;

namespace Murmur.Core.Tests.Input;

/// <summary>A keyboard layout that knows only the characters it is given, and writes down every key it was asked about.</summary>
internal sealed class FakeKeyboardLayout(IReadOnlyDictionary<int, char>? characters = null) : IKeyboardLayout
{
    /// <summary>A layout that cannot name any key.</summary>
    public static FakeKeyboardLayout Unmapped => new();

    /// <summary>The OEM keys of the Norwegian layout, as measured on the owner's PC (findings, section 11).</summary>
    public static FakeKeyboardLayout Nordic => new(new Dictionary<int, char>
    {
        [0xBA] = '¨',
        [0xBB] = '+',
        [0xBC] = ',',
        [0xBD] = '-',
        [0xBE] = '.',
        [0xBF] = '\'',
        [0xC0] = 'ø',
        [0xDB] = '\\',
        [0xDC] = '|',
        [0xDD] = 'å',
        [0xDE] = 'æ',
        [0xE2] = '<',
    });

    public List<int> Asked { get; } = [];

    public char? CharacterFor(int virtualKey)
    {
        Asked.Add(virtualKey);
        return characters is not null && characters.TryGetValue(virtualKey, out var character) ? character : null;
    }
}
