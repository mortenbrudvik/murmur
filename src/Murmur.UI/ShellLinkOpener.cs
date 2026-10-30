using System.Diagnostics;
using Murmur.Core.Services;
using Serilog;

namespace Murmur.UI;

/// <summary>Hands a link to the Windows shell, which opens it in the browser or the app registered for its scheme.</summary>
public sealed class ShellLinkOpener : ILinkOpener
{
    private readonly ILogger _logger = Log.ForContext<ShellLinkOpener>();

    public void Open(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not open {Uri}", uri);
        }
    }
}
