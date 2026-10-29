using System.Windows.Threading;

namespace Murmur.UI.Tests;

/// <summary>Runs WPF code on its own STA thread with a dispatcher, the way the app's UI thread runs it.</summary>
internal static class StaHost
{
    /// <summary>Runs <paramref name="body"/> on a new STA thread, then shuts that thread's dispatcher down and rethrows any exception.</summary>
    public static void Run(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new AggregateException(failure);
        }
    }

    /// <summary>Lets the current thread's dispatcher process queued work for <paramref name="duration"/>.</summary>
    public static void Pump(TimeSpan duration) => PumpUntil(() => false, duration);

    /// <summary>Processes queued work until <paramref name="condition"/> holds or <paramref name="timeout"/> passes; returns whether it held.</summary>
    public static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var poll = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(10) };
        var deadline = DateTime.UtcNow + timeout;
        poll.Tick += (_, _) =>
        {
            if (condition() || DateTime.UtcNow >= deadline)
            {
                frame.Continue = false;
            }
        };
        poll.Start();
        Dispatcher.PushFrame(frame);
        poll.Stop();
        return condition();
    }
}
