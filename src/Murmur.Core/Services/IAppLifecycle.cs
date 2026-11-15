namespace Murmur.Core.Services;

public interface IAppLifecycle
{
    /// <summary>
    /// Starts a fresh copy of the app and asks this one to exit, so that a changed environment (a new PATH) is picked
    /// up. Throws when the new copy cannot be started; this one then keeps running.
    /// </summary>
    void RequestRestart();

    /// <summary>Asks the app to exit, as the tray menu's Quit does.</summary>
    void RequestQuit();
}
