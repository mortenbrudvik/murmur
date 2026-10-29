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
        Log.ForContext(typeof(IpcLogger))
            .Debug("[IPC v] Event: {EventName} -> {Data}", eventName, Truncate(json));
    }

    private static string Truncate(string value, int maxLength = 200)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
