using System.Security.Authentication;

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
    /// No secure connection to the download server: the TLS handshake refused the server's certificate (a proxy,
    /// firewall or antivirus that inspects HTTPS with a certificate Windows does not trust, or a captive portal for a
    /// moment), or a proxy refused the tunnel or its sign-in. Retrying helps only with the captive portal. A handshake
    /// that failed for any other reason (the connection was reset, as on a lossy Wi-Fi link) is not one: .NET reports it
    /// as a SecureConnectionError too, but without an <see cref="AuthenticationException"/>.
    /// </summary>
    public static bool IsSecureConnectionFailure(Exception? ex) => ex switch
    {
        HttpRequestException request => IsSecureConnectionError(request.HttpRequestError, request),
        HttpIOException io => IsSecureConnectionError(io.HttpRequestError, io),
        _ => false,
    };

    private static bool IsSecureConnectionError(HttpRequestError error, Exception ex) => error switch
    {
        HttpRequestError.SecureConnectionError => RefusedCertificate(ex),
        HttpRequestError.ProxyTunnelError or HttpRequestError.UserAuthenticationError => true,
        _ => false,
    };

    private static bool RefusedCertificate(Exception ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is AuthenticationException)
                return true;
        }

        return false;
    }
}
