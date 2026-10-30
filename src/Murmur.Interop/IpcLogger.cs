using System.Diagnostics;
using Serilog;

namespace Murmur.Interop;

public static class IpcLogger
{
    [Conditional("DEBUG")]
    public static void LogRequest(string serviceName, string methodName, string jsonParams)
    {
        Log.ForContext(typeof(IpcLogger))
            .Debug("[IPC ->] {ServiceName}.{MethodName}({Params})", serviceName, methodName, Truncate(jsonParams));
    }

    [Conditional("DEBUG")]
    public static void LogResponse(string serviceName, string methodName, string jsonResult)
    {
        Log.ForContext(typeof(IpcLogger))
            .Debug("[IPC <-] {ServiceName}.{MethodName} -> {Result}", serviceName, methodName, Truncate(jsonResult));
    }

    [Conditional("DEBUG")]
    public static void LogEvent(string eventName, string json)
    {
        if (!LogsEvent(eventName))
            return;

        Log.ForContext(typeof(IpcLogger))
            .Debug("[IPC v] Event: {EventName} -> {Data}", eventName, Truncate(json));
    }

    /// <summary>
    /// Whether <see cref="LogEvent"/> logs this event: every one but the microphone test's level, which comes about 20
    /// times a second.
    /// </summary>
    public static bool LogsEvent(string eventName) => eventName != "setup.micLevel";

    private static string Truncate(string value, int maxLength = 200)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
