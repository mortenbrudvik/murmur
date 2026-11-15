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

    /// <summary>A Debug build logs to the installed app's folder, so what the user said must not end up there.</summary>
    [Theory]
    [InlineData("history")]
    [InlineData("vocabulary")]
    public void DescribePayload_UserTextService_OnlyLength(string serviceName)
    {
        const string json = "{\"success\":true,\"data\":[{\"text\":\"my secret dictation\"}]}";

        var described = IpcLogger.DescribePayload(serviceName, json);

        described.ShouldBe($"({json.Length} chars)");
        described.ShouldNotContain("secret");
    }

    [Fact]
    public void DescribeEventPayload_HistoryAdded_OnlyLength()
    {
        IpcLogger.DescribeEventPayload("history.added", "{\"text\":\"hello\"}").ShouldBe("(16 chars)");
    }

    [Fact]
    public void DescribeEventPayload_EngineStatus_ShowsPayload()
    {
        IpcLogger.DescribeEventPayload("engine.statusChanged", "\"ready\"").ShouldBe("\"ready\"");
    }

    [Fact]
    public void DescribePayload_OtherService_ShowsPayload()
    {
        IpcLogger.DescribePayload("settings", "{\"hotkey\":1}").ShouldBe("{\"hotkey\":1}");
    }

    [Fact]
    public void DescribePayload_LongPayload_TruncatedTo200Chars()
    {
        var json = new string('a', 250);

        IpcLogger.DescribePayload("engine", json).ShouldBe(new string('a', 200) + "...");
    }

    [Fact]
    public void DescribePayload_Null_Placeholder()
    {
        IpcLogger.DescribePayload("history", null).ShouldBe("(null)");
    }
}
