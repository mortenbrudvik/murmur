using Murmur.Core.Services;
using Shouldly;

namespace Murmur.Core.Tests.Services;

public sealed class PackageIdentityTests
{
    /// <summary>
    /// The test host is never packaged, so this pins the negative half of the probe: the call succeeds, interprets
    /// APPMODEL_ERROR_NO_PACKAGE, and reports unpackaged rather than throwing. Symptom if this regresses: an unpackaged
    /// Release build registers the packaged startup implementation, and start with Windows silently stops working.
    /// The packaged half is checked by running the staged package.
    /// </summary>
    [Fact]
    public void IsPackaged_InTheTestHost_IsFalse()
    {
        new PackageIdentity().IsPackaged.ShouldBeFalse();
    }
}
