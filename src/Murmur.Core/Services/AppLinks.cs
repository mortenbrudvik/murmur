using Murmur.Core.Models;

namespace Murmur.Core.Services;

/// <summary>
/// The only links the web UI can have opened. It names a <see cref="LinkTarget"/> and never passes an address, so the
/// page cannot make Murmur launch anything else.
/// </summary>
public static class AppLinks
{
    public static string For(LinkTarget target) => target switch
    {
        LinkTarget.CudaDownload => "https://developer.nvidia.com/cuda-downloads",
        LinkTarget.NvidiaDriver => "https://www.nvidia.com/Download/index.aspx",
        LinkTarget.MicrophonePrivacy => "ms-settings:privacy-microphone",
        LinkTarget.Repository => "https://github.com/mortenbrudvik/murmur",
        LinkTarget.PrivacyPolicy => "https://brudvik.com/murmur/privacy",
        LinkTarget.License => "https://github.com/mortenbrudvik/murmur/blob/main/LICENSE",
        LinkTarget.HomePage => "https://brudvik.com",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown link target."),
    };
}
