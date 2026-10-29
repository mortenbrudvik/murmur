namespace Murmur.Core.Input;

/// <summary>Reads the physical keyboard state directly, independent of the hook's event stream.</summary>
public interface IKeyStateProbe
{
    bool IsKeyDown(int virtualKey);

    /// <summary>True while any Shift, Ctrl, Alt or Win key is held.</summary>
    bool IsAnyModifierDown();
}
