using Shouldly;

namespace Murmur.UI.Tests;

/// <summary>Uses kernel-object names of its own, so it never interferes with a running Murmur.</summary>
public sealed class SingleInstanceGuardTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly string _mutexName = @"Local\Murmur.Tests.Mutex." + Guid.NewGuid().ToString("N");
    private readonly string _eventName = @"Local\Murmur.Tests.Show." + Guid.NewGuid().ToString("N");

    [Fact]
    public void TryAcquire_FirstInstance_True()
    {
        using var first = NewGuard();

        first.TryAcquire().ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquire_WhileAnotherInstanceRuns_False()
    {
        using var first = NewGuard();
        first.TryAcquire();

        // Another thread: a mutex can be re-acquired by the thread that already owns it.
        var second = await Task.Run(() =>
        {
            using var guard = NewGuard();
            return guard.TryAcquire();
        });

        second.ShouldBeFalse();
    }

    [Fact]
    public async Task TryAcquire_AfterFirstInstanceExits_True()
    {
        var first = NewGuard();
        first.TryAcquire();
        first.Dispose();

        var second = await Task.Run(() =>
        {
            using var guard = NewGuard();
            return guard.TryAcquire();
        });

        second.ShouldBeTrue();
    }

    [Fact]
    public void SignalExisting_RunsTheRunningInstancesListener()
    {
        using var first = NewGuard();
        first.TryAcquire();
        using var shown = new ManualResetEventSlim();
        first.ListenForShowRequests(shown.Set);

        using (var second = NewGuard())
            second.SignalExisting();

        shown.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
    }

    [Fact]
    public void SignalExisting_BeforeTheRunningInstanceListens_IsStillDelivered()
    {
        using var first = NewGuard();
        first.TryAcquire();
        using (var second = NewGuard())
            second.SignalExisting();

        using var shown = new ManualResetEventSlim();
        first.ListenForShowRequests(shown.Set);

        shown.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
    }

    private SingleInstanceGuard NewGuard() => new(_mutexName, _eventName);
}
