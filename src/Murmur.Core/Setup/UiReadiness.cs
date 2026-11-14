namespace Murmur.Core.Setup;

/// <summary>
/// Whether the web UI has drawn its first page. Set once, by the UI's own call (<c>app</c> / <c>ready</c>); the splash
/// waits for it, and a request to open the About dialog is held back until then.
/// </summary>
public sealed class UiReadiness
{
    private int _ready;

    public bool IsReady => Volatile.Read(ref _ready) == 1;

    /// <summary>
    /// Raised once, on the thread that called <see cref="MarkReady"/>, after <see cref="IsReady"/> has become true.
    /// A handler added after that is never called: check <see cref="IsReady"/> as well.
    /// </summary>
    public event Action? BecameReady;

    /// <summary>Safe to call from any thread, any number of times.</summary>
    public void MarkReady()
    {
        if (Interlocked.Exchange(ref _ready, 1) == 0)
            BecameReady?.Invoke();
    }
}
