using System.Runtime.InteropServices;
using Mediator;
using Murmur.Common;
using Murmur.Core.Audio;
using Serilog;

namespace Murmur.Interop;

/// <summary>What the page sees of the bridge: this one method, and nothing the class inherits from object.</summary>
[ComVisible(true)]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IApiBridge
{
    Task<string> InvokeMethod(string serviceName, string methodName, string jsonParams);
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public class ApiBridge(IMediator mediator, ApiRegistry apiRegistry) : IApiBridge
{
    /// <summary>What the page is told when a call fails in a way that is not the user's to fix; the log has the rest.</summary>
    public const string UnexpectedError = "Something went wrong. The log has the details.";

    public async Task<string> InvokeMethod(string serviceName, string methodName, string jsonParams)
    {
        try
        {
            // Inside the try: a caller that is not the web app's client can pass a null string, and the request log
            // must not turn that into an exception the caller sees instead of an error response.
            IpcLogger.LogRequest(serviceName, methodName, jsonParams);
            var requestType = apiRegistry.GetRequestType(serviceName, methodName);
            if (requestType == null)
                return JsonUtils.Serialize(new { success = false, error = $"Unknown API: {serviceName}.{methodName}" });

            var request = JsonUtils.Deserialize(jsonParams, requestType);
            if (request == null)
                return JsonUtils.Serialize(new { success = false, error = $"Invalid params for {serviceName}.{methodName}" });

            // Task.Run keeps handlers off the UI thread: WebView2 calls in on it, and the generated Send(object) awaits
            // without ConfigureAwait(false), so handlers would otherwise resume on the dispatcher.
            // AsTask: Task.Run has no ValueTask overload, so without it the await would yield the ValueTask itself and
            // the web app would get that, not the response.
            var result = await Task.Run(() => mediator.Send(request).AsTask()).ConfigureAwait(false);
            var json = JsonUtils.Serialize(new { success = true, data = result });
            IpcLogger.LogResponse(serviceName, methodName, json);
            return json;
        }
        catch (Exception ex)
        {
            Log.ForContext<ApiBridge>().Error(ex, "API call failed: {ServiceName}.{MethodName}", serviceName, methodName);
            var errorJson = JsonUtils.Serialize(new { success = false, error = ErrorMessage(ex) });
            IpcLogger.LogResponse(serviceName, methodName, errorJson);
            return errorJson;
        }
    }

    /// <summary>
    /// The message the page shows for a failed call. Only exceptions written for people pass theirs on: a request the
    /// handler refused (an <see cref="ArgumentException"/> naming no parameter, such as a vocabulary limit), a file
    /// that could not be read or written (Windows says why: in use, disk full, access denied) and a microphone that
    /// could not be opened. Anything else is a bug, whose message would only puzzle the user.
    /// </summary>
    internal static string ErrorMessage(Exception ex) => ex switch
    {
        ArgumentException { ParamName: null } or IOException or UnauthorizedAccessException or AudioDeviceException => ex.Message,
        _ => UnexpectedError,
    };
}
