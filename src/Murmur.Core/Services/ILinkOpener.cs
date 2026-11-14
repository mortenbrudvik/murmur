namespace Murmur.Core.Services;

/// <summary>Opens a link in whatever Windows has registered for it: a web page in the browser, ms-settings: in Settings.</summary>
public interface ILinkOpener
{
    /// <summary>
    /// Throws when the shell cannot open the link (no app is registered for it, or the path does not exist), so the
    /// caller can say so.
    /// </summary>
    void Open(string uri);
}
