using System.Windows.Threading;

namespace Murmur.UI;

/// <summary>Puts work off for one turn of the dispatcher, so that what is waiting to be drawn is drawn first.</summary>
internal static class StartupTurn
{
    /// <summary>
    /// Runs <paramref name="work"/> once pending layout and rendering are done, and yet as a top-priority operation.
    /// <para>
    /// Two steps, because they want different priorities. Waiting has to be below rendering, or the work would run
    /// before the splash is drawn. But WPF gives each operation a SynchronizationContext at that operation's own
    /// priority, and start-up captures it for the life of the process: the dictation controller posts recorder
    /// events and timer ticks through it. Work run in the Background operation itself would post all of that below
    /// rendering and input for ever after (measured). So the Background operation only queues the real one, at Send,
    /// which is the priority start-up has when it runs straight from OnStartup.
    /// </para>
    /// </summary>
    public static void AfterRender(Dispatcher dispatcher, Action work) =>
        dispatcher.BeginInvoke(DispatcherPriority.Background, () => dispatcher.BeginInvoke(DispatcherPriority.Send, work));
}
