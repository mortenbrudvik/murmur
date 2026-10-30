namespace Murmur.Core.Services;

public interface IAppLifecycle
{
    /// <summary>Asks the app to start a fresh copy of itself and exit, so that a changed environment (a new PATH) is picked up.</summary>
    void RequestRestart();
}
