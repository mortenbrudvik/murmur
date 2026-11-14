using System.Diagnostics;
using Murmur.Core.Services;

namespace Murmur.UI;

/// <summary>Hands a link to the Windows shell, which opens it in the browser or the app registered for its scheme.</summary>
public sealed class ShellLinkOpener : ILinkOpener
{
    public void Open(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
}
