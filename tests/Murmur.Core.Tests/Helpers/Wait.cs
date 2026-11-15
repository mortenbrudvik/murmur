using System.Diagnostics;

namespace Murmur.Core.Tests.Helpers;

internal static class Wait
{
    /// <summary>Polls in real time for something another thread is about to do; throws once <paramref name="patience"/> has passed.</summary>
    public static async Task UntilAsync(Func<bool> condition, TimeSpan patience, string what = "the condition")
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > patience)
                throw new TimeoutException($"Timed out waiting for {what}.");

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
