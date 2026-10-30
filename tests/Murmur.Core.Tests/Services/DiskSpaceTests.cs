using Murmur.Core.Services;
using Shouldly;

namespace Murmur.Core.Tests.Services;

public sealed class DiskSpaceTests
{
    [Fact]
    public void GetAvailableFreeSpace_LocalFolder_MeasuresTheDrive()
    {
        var free = new DiskSpace().GetAvailableFreeSpace(Path.GetTempPath());

        free.ShouldBeInRange(0, long.MaxValue - 1);
    }

    /// <summary>DriveInfo takes no UNC root, so a models folder on a share cannot be measured: the check is skipped.</summary>
    [Fact]
    public void GetAvailableFreeSpace_UncPath_SkipsTheCheck()
    {
        var free = Should.NotThrow(() => new DiskSpace().GetAvailableFreeSpace(@"\\server\share\models"));

        free.ShouldBe(long.MaxValue);
    }
}
