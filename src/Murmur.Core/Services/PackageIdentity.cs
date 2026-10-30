using Murmur.Core.Interop;

namespace Murmur.Core.Services;

/// <summary>
/// The probe, over <c>GetCurrentPackageFullName</c>. <c>Package.Current</c> is deliberately not used: it throws when
/// there is no identity, which would make the ordinary unpackaged case an exception.
/// </summary>
public sealed class PackageIdentity : IPackageIdentity
{
    public bool IsPackaged { get; } = HasPackage();

    private static unsafe bool HasPackage()
    {
        uint length = 0;
        return NativeMethods.GetCurrentPackageFullName(ref length, null) != NativeMethods.APPMODEL_ERROR_NO_PACKAGE;
    }
}
