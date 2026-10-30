using Murmur.Interop;
using Shouldly;

namespace Murmur.Interop.Tests;

public class IpcLoggerTests
{
    /// <summary>The microphone test sends about 20 levels a second; a line for each would drown the Debug log.</summary>
    [Fact]
    public void LogsEvent_MicLevel_IsNotLogged()
    {
        IpcLogger.LogsEvent("setup.micLevel").ShouldBeFalse();
    }

    [Theory]
    [InlineData("engine.statusChanged")]
    [InlineData("history.added")]
    public void LogsEvent_OtherEvents_AreLogged(string eventName)
    {
        IpcLogger.LogsEvent(eventName).ShouldBeTrue();
    }
}
