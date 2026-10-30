namespace Murmur.Core.Services;

/// <summary>Free space on the drive that holds a folder, behind an interface so a full disk can be tested.</summary>
public interface IDiskSpace
{
    /// <summary>
    /// Bytes the current user can still write to the drive that holds <paramref name="directory"/>, or
    /// <see cref="long.MaxValue"/> when that cannot be told (a network share, say). Never throws.
    /// </summary>
    long GetAvailableFreeSpace(string directory);
}
