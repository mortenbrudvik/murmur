using System.Diagnostics;
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

    /// <summary>A restarted Murmur starts before the old one has left; it waits for the old one to let go.</summary>
    [Fact]
    public void TryAcquire_MutexReleasedWithinTheWait_Succeeds()
    {
        using var other = HoldOnAnotherThread(releaseAfter: TimeSpan.FromMilliseconds(200));
        using var guard = NewGuard();

        guard.TryAcquire(Timeout).ShouldBeTrue();
    }

    [Fact]
    public void TryAcquire_MutexStillHeld_FailsAfterTheWait()
    {
        using var other = HoldOnAnotherThread(releaseAfter: null);
        using var guard = NewGuard();
        var started = Stopwatch.StartNew();

        guard.TryAcquire(TimeSpan.FromMilliseconds(300)).ShouldBeFalse();

        started.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void TryAcquire_PreviousInstanceCrashedHoldingTheMutex_Succeeds()
    {
        using var mutex = new Mutex(initiallyOwned: false, _mutexName);
        var crash = new Thread(() => mutex.WaitOne()); // ends without releasing: the mutex is abandoned
        crash.Start();
        crash.Join();
        using var guard = NewGuard();

        guard.TryAcquire(Timeout).ShouldBeTrue();
    }

    private SingleInstanceGuard NewGuard() => new(_mutexName, _eventName);

    /// <summary>
    /// Another instance: acquires the mutex on a thread of its own (a mutex belongs to the thread that took it) and
    /// releases it on that thread, after <paramref name="releaseAfter"/> or, when null, when disposed.
    /// </summary>
    private OtherInstance HoldOnAnotherThread(TimeSpan? releaseAfter)
    {
        var other = new OtherInstance(NewGuard(), releaseAfter);
        other.WaitUntilHolding(Timeout);
        return other;
    }

    private sealed class OtherInstance : IDisposable
    {
        private readonly ManualResetEventSlim _holding = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;
        private Exception? _failure;

        public OtherInstance(SingleInstanceGuard guard, TimeSpan? releaseAfter)
        {
            _thread = new Thread(() =>
            {
                try
                {
                    _failure = guard.TryAcquire() ? null : new InvalidOperationException("The other instance could not take the mutex.");
                    _holding.Set();
                    _release.Wait(releaseAfter ?? System.Threading.Timeout.InfiniteTimeSpan);
                }
                catch (Exception ex)
                {
                    _failure = ex;
                    _holding.Set();
                }
                finally
                {
                    guard.Dispose(); // on the thread that acquired it
                }
            })
            {
                IsBackground = true,
            };
            _thread.Start();
        }

        public void WaitUntilHolding(TimeSpan timeout)
        {
            _holding.Wait(timeout).ShouldBeTrue("the other instance took the mutex");
            _failure.ShouldBeNull();
        }

        public void Dispose()
        {
            _release.Set();
            _thread.Join();
            _holding.Dispose();
            _release.Dispose();
        }
    }
}
