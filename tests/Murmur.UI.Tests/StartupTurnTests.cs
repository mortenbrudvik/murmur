using System.Windows.Threading;
using Shouldly;

namespace Murmur.UI.Tests;

public sealed class StartupTurnTests
{
    /// <summary>The point of the wait: what is waiting to be drawn (the splash) is drawn before the work runs.</summary>
    [Fact]
    public void AfterRender_RunsTheWorkAfterPendingRendering()
    {
        StaHost.Run(() =>
        {
            var order = new List<string>();
            var dispatcher = Dispatcher.CurrentDispatcher;

            StartupTurn.AfterRender(dispatcher, () => order.Add("work"));
            dispatcher.BeginInvoke(DispatcherPriority.Render, () => order.Add("render"));
            order.ShouldBeEmpty();

            StaHost.PumpUntil(() => order.Count == 2, TimeSpan.FromSeconds(2)).ShouldBeTrue();
            order.ShouldBe(["render", "work"]);
        });
    }

    /// <summary>
    /// The rest of start-up captures SynchronizationContext.Current for the life of the process: the dictation
    /// controller posts recorder events and timer ticks through it, and the single-instance guard its show requests.
    /// WPF gives each dispatcher operation a context at that operation's priority, so work that merely ran in a
    /// Background operation would post everything below rendering and input from then on. It must post at the top
    /// priority, as it does when start-up runs straight from OnStartup (a sign-in start). Symptom if this regresses:
    /// dictation events queue behind rendering after a launch by hand, and not after a sign-in start.
    /// </summary>
    [Fact]
    public void AfterRender_TheWorksSynchronizationContextPostsAheadOfNormalWork()
    {
        StaHost.Run(() =>
        {
            var order = new List<string>();
            var dispatcher = Dispatcher.CurrentDispatcher;

            StartupTurn.AfterRender(dispatcher, () =>
            {
                var context = SynchronizationContext.Current.ShouldNotBeNull();
                dispatcher.BeginInvoke(DispatcherPriority.Normal, () => order.Add("normal"));
                context.Post(_ => order.Add("posted"), null);
            });

            StaHost.PumpUntil(() => order.Count == 2, TimeSpan.FromSeconds(2)).ShouldBeTrue();
            order.ShouldBe(["posted", "normal"]);
        });
    }
}
