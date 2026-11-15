using Serilog;

namespace Murmur.Common;

public static class AsyncUtils
{
    /// <summary>Lets <paramref name="t"/> run on unwatched; if it faults, the fault is logged instead of going unobserved.</summary>
    public static void FireAndForget(this Task t) => _ = t.LogFault(logger: null);

    /// <summary>
    /// As <see cref="FireAndForget(Task)"/>, for a <see cref="ValueTask"/> (what Mediator's Publish returns).
    /// </summary>
    public static void FireAndForget(this ValueTask t) => _ = t.LogFault(logger: null);

    /// <summary>
    /// The continuation behind <see cref="FireAndForget(Task)"/>: logs <paramref name="t"/>'s fault, if any, to
    /// <paramref name="logger"/> (null: the global logger at that moment). It always completes, whether <paramref name="t"/>
    /// succeeded, faulted or was cancelled, so a test can wait for it instead of sleeping.
    /// </summary>
    internal static Task LogFault(this Task t, ILogger? logger) =>
        t.ContinueWith(
            static (task, state) =>
            {
                if (task.IsFaulted)
                    ((ILogger?)state ?? Log.Logger).Error(task.Exception, "FireAndForget task faulted");
            },
            logger,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>As <see cref="LogFault(Task, ILogger?)"/>, for a <see cref="ValueTask"/>.</summary>
    internal static Task LogFault(this ValueTask t, ILogger? logger) => t.AsTask().LogFault(logger);
}
