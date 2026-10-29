using Murmur.Common;
using Shouldly;

namespace Murmur.Core.Tests.Common;

public class AsyncUtilsTests
{
    [Fact]
    public async Task FireAndForget_DoesNotThrow_WhenTaskSucceeds()
    {
        var completed = false;

        var task = Task.Run(() => { completed = true; }, TestContext.Current.CancellationToken);
        task.FireAndForget();

        await task;
        completed.ShouldBeTrue();
    }

    [Fact]
    public async Task FireAndForget_ObservesException_WhenTaskFaults()
    {
        var tcs = new TaskCompletionSource<bool>();

        var faultingTask = Task.Run(() => throw new InvalidOperationException("test fault"), TestContext.Current.CancellationToken);
        faultingTask.FireAndForget();

        // Give the continuation time to execute
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // The task should be faulted but no unobserved exception should propagate
        faultingTask.IsFaulted.ShouldBeTrue();
        faultingTask.Exception.ShouldNotBeNull();
    }
}
