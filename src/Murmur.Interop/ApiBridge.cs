using System.Runtime.InteropServices;
using Mediator;
using Murmur.Common;
using Serilog;

namespace Murmur.Interop;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class ApiBridge(IMediator mediator, ApiRegistry apiRegistry)
{
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
            var errorJson = JsonUtils.Serialize(new { success = false, error = ex.Message });
            IpcLogger.LogResponse(serviceName, methodName, errorJson);
            return errorJson;
        }
    }
}
