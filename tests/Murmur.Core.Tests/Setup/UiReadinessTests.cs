using Murmur.Core.Setup;
using Shouldly;

namespace Murmur.Core.Tests.Setup;

public sealed class UiReadinessTests
{
    [Fact]
    public void NewOne_IsNotReady()
    {
        new UiReadiness().IsReady.ShouldBeFalse();
    }

    [Fact]
    public void MarkReady_RaisesBecameReadyOnce_AndSetsIsReady()
    {
        var ui = new UiReadiness();
        var raised = 0;
        ui.BecameReady += () => raised++;

        ui.MarkReady();
        ui.MarkReady();

        ui.IsReady.ShouldBeTrue();
        raised.ShouldBe(1);
    }

    /// <summary>Whoever handles BecameReady must already find the UI ready, or it would queue what it could send.</summary>
    [Fact]
    public void MarkReady_IsReadyIsAlreadyTrueInsideTheHandler()
    {
        var ui = new UiReadiness();
        bool? readyInHandler = null;
        ui.BecameReady += () => readyInHandler = ui.IsReady;

        ui.MarkReady();

        readyInHandler.ShouldBe(true);
    }

    [Fact]
    public async Task MarkReady_FromManyThreads_RaisesOnce()
    {
        var ui = new UiReadiness();
        var raised = 0;
        ui.BecameReady += () => Interlocked.Increment(ref raised);
        using var start = new ManualResetEventSlim();

        var callers = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            start.Wait();
            ui.MarkReady();
        })).ToArray();
        start.Set();
        await Task.WhenAll(callers);

        raised.ShouldBe(1);
    }
}
