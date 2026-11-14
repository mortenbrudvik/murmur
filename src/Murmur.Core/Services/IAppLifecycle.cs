namespace Murmur.Core.Services;

public interface IAppLifecycle
{
    /// <summary>Asks the app to start a fresh copy of itself and exit, so that a changed environment (a new PATH) is picked up.</summary>
    void RequestRestart();

    /// <summary>Asks the app to exit, as the tray menu's Quit does.</summary>
    void RequestQuit();
}
