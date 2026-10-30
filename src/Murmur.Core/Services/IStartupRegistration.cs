namespace Murmur.Core.Services;

/// <summary>
/// Whether Murmur starts when the owner signs in to Windows (per user, no admin rights needed): the Run key when
/// unpackaged (<see cref="StartupRegistration"/>), the package's startup task in the Store build
/// (<see cref="PackagedStartupRegistration"/>).
/// </summary>
public interface IStartupRegistration
{
    /// <summary>
    /// Adds (<paramref name="enabled"/>) or removes the startup entry. Idempotent. The registry implementation throws the
    /// exceptions below; the packaged one finishes in the background and never throws.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The registry key cannot be written (for example by policy).</exception>
    /// <exception cref="System.Security.SecurityException">The registry key cannot be written (for example by policy).</exception>
    /// <exception cref="IOException">The registry key cannot be written.</exception>
    void Apply(bool enabled);

    /// <summary>True when the startup entry exists and starts this executable.</summary>
    bool IsRegistered { get; }
}
