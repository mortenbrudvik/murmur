using Murmur.Common;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Shouldly;

namespace Murmur.Core.Tests.Common;

public class AsyncUtilsTests
{
    private readonly CapturingSink _sink = new();
    private readonly ILogger _logger;

    public AsyncUtilsTests()
    {
        _logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
    }

    [Fact]
    public async Task LogFault_TaskFaults_LogsTheExceptionOnce()
    {
        var fault = new InvalidOperationException("test fault");

        await Task.FromException(fault).LogFault(_logger);

        var logged = _sink.Events.ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogEventLevel.Error);
        logged.Exception.ShouldBeOfType<AggregateException>().InnerException.ShouldBeSameAs(fault);
    }

    [Fact]
    public async Task LogFault_TaskSucceeds_LogsNothing()
    {
        await Task.CompletedTask.LogFault(_logger);

        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task LogFault_TaskCancelled_LogsNothing()
    {
        await Task.FromCanceled(new CancellationToken(canceled: true)).LogFault(_logger);

        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task LogFault_TaskStillRunning_LogsOnlyOnceItFaults()
    {
        var running = new TaskCompletionSource();
        var logging = running.Task.LogFault(_logger);
        _sink.Events.ShouldBeEmpty();

        running.SetException(new IOException("late"));
        await logging;

        _sink.Events.ShouldHaveSingleItem().Exception!.InnerException.ShouldBeOfType<IOException>();
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_events)
                    return [.. _events];
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events)
                _events.Add(logEvent);
        }
    }
}
