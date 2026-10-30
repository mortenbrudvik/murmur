using Serilog;

namespace Murmur.Core.Services;

public sealed class DiskSpace : IDiskSpace
{
    private readonly ILogger _logger = Log.ForContext<DiskSpace>();

    /// <summary>
    /// The drive's free space, or <see cref="long.MaxValue"/> when it cannot be measured (DriveInfo takes no UNC root,
    /// for one), so the check is skipped instead of failing the download.
    /// </summary>
    public long GetAvailableFreeSpace(string directory)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _logger.Warning(ex, "Could not tell the free space for {Directory}; downloading without checking it", directory);
            return long.MaxValue;
        }
    }
}
