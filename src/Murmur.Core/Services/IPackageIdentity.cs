namespace Murmur.Core.Services;

/// <summary>
/// Whether this process runs from an MSIX package (the Microsoft Store build). Decides which
/// <see cref="IStartupRegistration"/> is registered, because the HKCU Run key does not work inside a package and does
/// not fail either.
/// </summary>
public interface IPackageIdentity
{
    bool IsPackaged { get; }
}
