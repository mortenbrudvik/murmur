namespace Murmur.Core.Services;

public sealed class DiskSpace : IDiskSpace
{
    public long GetAvailableFreeSpace(string directory) =>
        new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace;
}
