namespace Murmur.Core.Services;

/// <summary>Whether Murmur starts when the owner signs in to Windows (per user, no admin rights needed).</summary>
public interface IStartupRegistration
{
    /// <summary>Adds (<paramref name="enabled"/>) or removes the startup entry. Idempotent.</summary>
    /// <exception cref="UnauthorizedAccessException">The registry key cannot be written (for example by policy).</exception>
    /// <exception cref="System.Security.SecurityException">The registry key cannot be written (for example by policy).</exception>
    /// <exception cref="IOException">The registry key cannot be written.</exception>
    void Apply(bool enabled);

    /// <summary>True when the startup entry exists and starts this executable.</summary>
    bool IsRegistered { get; }
}
