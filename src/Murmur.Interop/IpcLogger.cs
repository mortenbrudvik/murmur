using System.Diagnostics;
using Serilog;

namespace Murmur.Interop;

public static class IpcLogger
{
    private const int MaxLoggedLength = 200;

    private static ILogger Logger => Log.ForContext(typeof(IpcLogger));

    [Conditional("DEBUG")]
    public static void LogRequest(string serviceName, string methodName, string jsonParams)
    {
        Logger.Debug("[IPC ->] {ServiceName}.{MethodName}({Params})", serviceName, methodName,
                DescribePayload(serviceName, jsonParams));
    }

    [Conditional("DEBUG")]
    public static void LogResponse(string serviceName, string methodName, string jsonResult)
    {
        Logger.Debug("[IPC <-] {ServiceName}.{MethodName} -> {Result}", serviceName, methodName,
                DescribePayload(serviceName, jsonResult));
    }

    [Conditional("DEBUG")]
    public static void LogEvent(string eventName, string json)
    {
        if (!LogsEvent(eventName))
            return;

        Logger.Debug("[IPC v] Event: {EventName} -> {Data}", eventName, DescribeEventPayload(eventName, json));
    }

    /// <summary>
    /// Whether <see cref="LogEvent"/> logs this event: every one but the microphone test's level, which comes about 20
    /// times a second.
    /// </summary>
    public static bool LogsEvent(string eventName) => eventName != IpcNames.Events.MicLevel;

    /// <summary>
    /// What the log shows of a payload to or from <paramref name="serviceName"/>. The history and vocabulary services
    /// carry what the user said and the words they added, and a Debug build writes its log to the same folder as the
    /// installed app, so for those only the length is shown. Any other payload is shown up to 200 characters.
    /// </summary>
    public static string DescribePayload(string serviceName, string? json)
    {
        if (json is null)
            return "(null)";
        if (CarriesUserText(serviceName))
            return $"({json.Length} chars)";
        return json.Length <= MaxLoggedLength ? json : json[..MaxLoggedLength] + "...";
    }

    /// <summary>
    /// What the log shows of an event's payload: as <see cref="DescribePayload"/> for the service the event's name
    /// starts with, so <c>history.added</c> shows only its length.
    /// </summary>
    public static string DescribeEventPayload(string eventName, string? json)
        => DescribePayload(ServiceOf(eventName), json);

    private static bool CarriesUserText(string serviceName) =>
        serviceName is IpcNames.Services.History or IpcNames.Services.Vocabulary;

    /// <summary>The service part of an event name such as <c>history.added</c>.</summary>
    private static string ServiceOf(string eventName)
    {
        var dot = eventName.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? eventName : eventName[..dot];
    }
}
