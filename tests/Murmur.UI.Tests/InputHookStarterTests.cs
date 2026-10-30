using System.ComponentModel;
using Murmur.Core.Input;
using Murmur.Core.Insertion;
using Murmur.Core.Models;
using NSubstitute;
using Shouldly;

namespace Murmur.UI.Tests;

public sealed class InputHookStarterTests
{
    private readonly List<string> _log = [];
    private readonly ManualTimeProvider _time = new();

    /// <summary>
    /// A first attempt that timed out may still install its hook later. It is disposed (its thread told to quit) before
    /// the second host exists, so the two never run side by side and no key reaches the dictation twice.
    /// </summary>
    [Fact]
    public async Task Start_FirstAttemptFails_DisposesItBeforeRetrying()
    {
        var hosts = new Queue<InputHookHost>([
            NewHost("first", new Win32Exception(5)),
            NewHost("second"),
        ]);
        var starter = new InputHookStarter(() =>
        {
            _log.Add("create");
            return hosts.Dequeue();
        }, _time);

        var pending = starter.StartAsync();

        // The wait runs on the clock the starter was given: nothing happens until it moves.
        pending.IsCompleted.ShouldBeFalse();
        _log.ShouldBe(["create", "first.Start", "first.Dispose"]);
        _time.Delays.ShouldBe([TimeSpan.FromSeconds(2)]);

        _time.FireTimers();
        var host = await pending;

        _log.ShouldBe(["create", "first.Start", "first.Dispose", "create", "second.Start"]);
        host.ShouldNotBeNull().ShouldBeOfType<RecordingHost>().Name.ShouldBe("second");
        starter.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task Start_FirstAttemptSucceeds_StartsOnceAndDoesNotWait()
    {
        var starter = new InputHookStarter(() => NewHost("only"), _time);

        var host = await starter.StartAsync();

        host.ShouldNotBeNull().ShouldBeOfType<RecordingHost>().Name.ShouldBe("only");
        _log.ShouldBe(["only.Start"]);
        _time.Delays.ShouldBeEmpty();
        starter.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task Start_BothAttemptsFail_ReturnsNull()
    {
        var attempts = 0;
        var starter = new InputHookStarter(() => ++attempts == 1
            ? NewHost("first", new Win32Exception(5))
            : NewHost("second", new TimeoutException("The input hook thread did not start in time.")), _time);

        var pending = starter.StartAsync();
        _time.FireTimers();
        var host = await pending;

        host.ShouldBeNull();
        // Each failed host is let go of, so neither is left holding a hook thread.
        _log.ShouldBe(["first.Start", "first.Dispose", "second.Start", "second.Dispose"]);
        // The reason is the last one, for the status the web app reads.
        starter.Failure.ShouldBeOfType<TimeoutException>();
    }

    /// <summary>A host that cannot even be built (a service failing to resolve) is a failed attempt like any other.</summary>
    [Fact]
    public async Task Start_TheFactoryThrows_CountsAsAFailedAttempt()
    {
        var calls = 0;
        var starter = new InputHookStarter(() =>
        {
            calls++;
            throw new InvalidOperationException("No hook for you.");
        }, _time);

        var pending = starter.StartAsync();
        _time.FireTimers();
        var host = await pending;

        host.ShouldBeNull();
        calls.ShouldBe(2);
        starter.Failure.ShouldBeOfType<InvalidOperationException>();
    }

    private RecordingHost NewHost(string name, Exception? failure = null) => new(name, _log, failure);

    /// <summary>A host whose hook thread is never started: Start fails or succeeds as told, and both are written down.</summary>
    private sealed class RecordingHost(string name, List<string> log, Exception? failure)
        : InputHookHost(
            new HotkeyMatcher(HotkeyDefinition.Default),
            new HotkeyCaptureCoordinator(TimeProvider.System),
            Substitute.For<IKeyboardSender>(),
            TimeProvider.System,
            static (_, _) => { })
    {
        public string Name { get; } = name;

        public override void Start()
        {
            log.Add($"{Name}.Start");
            if (failure is not null)
                throw failure;
        }

        public override void Dispose() => log.Add($"{Name}.Dispose");
    }

    /// <summary>A clock whose timers only fire when the test says so.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public IReadOnlyList<TimeSpan> Delays => _timers.ConvertAll(timer => timer.DueTime);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            _timers.Add(timer);
            return timer;
        }

        public void FireTimers()
        {
            foreach (var timer in _timers.ToArray())
                timer.Fire();
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        public TimeSpan DueTime { get; private set; } = dueTime;

        public bool Change(TimeSpan newDueTime, TimeSpan newPeriod)
        {
            DueTime = newDueTime;
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
