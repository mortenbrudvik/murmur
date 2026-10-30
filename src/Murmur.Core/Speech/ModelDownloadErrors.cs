namespace Murmur.Core.Speech;

/// <summary>The drive has less free space than the download needs, so none of it was started.</summary>
public sealed class InsufficientDiskSpaceException(long requiredBytes, long availableBytes, string directory)
    : IOException($"Not enough disk space in {directory}: {requiredBytes:N0} bytes are needed and {availableBytes:N0} are free.")
{
    public long RequiredBytes { get; } = requiredBytes;

    public long AvailableBytes { get; } = availableBytes;

    public string Directory { get; } = directory;
}

/// <summary>The downloaded file does not match the pinned SHA-256, even after downloading it again.</summary>
public sealed class ModelVerificationException(string message) : IOException(message);
