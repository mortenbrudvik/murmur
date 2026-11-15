using Murmur.Core.Input;

namespace Murmur.Core.Tests.Input;

/// <summary>
/// A key-state probe that never contradicts the hook's event stream: it reports every key as down, so a matcher given
/// it keeps every modifier it has tracked. For tests that are not about a missed key-up.
/// </summary>
internal sealed class AllKeysDown : IKeyStateProbe
{
    public static AllKeysDown Instance { get; } = new();

    public bool IsKeyDown(int virtualKey) => true;

    public bool IsAnyModifierDown() => true;
}
