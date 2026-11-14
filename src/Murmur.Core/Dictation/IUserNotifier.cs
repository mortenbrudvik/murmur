namespace Murmur.Core.Dictation;

/// <summary>
/// A Windows notification (a toast through the tray icon), for news that must outlast the pill: the pill is
/// click-through and gone after a few seconds, while a notification stays in the notification centre. Implementations
/// may be called from any thread and never throw.
/// </summary>
public interface IUserNotifier
{
    void Notify(string title, string message);
}
