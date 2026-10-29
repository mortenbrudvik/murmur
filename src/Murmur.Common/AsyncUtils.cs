using Serilog;

namespace Murmur.Common;

public static class AsyncUtils
{
    public static void FireAndForget(this Task t)
    {
        t.ContinueWith(tsk =>
        {
            Log.Error(tsk.Exception, "FireAndForget task faulted");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
