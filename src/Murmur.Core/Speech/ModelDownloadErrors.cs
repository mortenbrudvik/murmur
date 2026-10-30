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

/// <summary>Sorts download failures, for the model manager's retries and the speech engine's messages.</summary>
internal static class DownloadFailures
{
    /// <summary>
    /// No secure connection to the download server: the TLS handshake failed (a proxy, firewall or antivirus that
    /// inspects HTTPS with a certificate Windows does not trust, or a captive portal for a moment), or a proxy refused
    /// the tunnel or its sign-in. Retrying helps only with the captive portal.
    /// </summary>
    public static bool IsSecureConnectionFailure(Exception? ex) => ex switch
    {
        HttpRequestException request => IsSecureConnectionError(request.HttpRequestError),
        HttpIOException io => IsSecureConnectionError(io.HttpRequestError),
        _ => false,
    };

    private static bool IsSecureConnectionError(HttpRequestError error) =>
        error is HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError or HttpRequestError.UserAuthenticationError;
}
