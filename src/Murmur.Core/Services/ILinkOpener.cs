namespace Murmur.Core.Services;

/// <summary>Opens a link in whatever Windows has registered for it: a web page in the browser, ms-settings: in Settings.</summary>
public interface ILinkOpener
{
    /// <summary>Never throws: a link that cannot be opened is logged.</summary>
    void Open(string uri);
}
