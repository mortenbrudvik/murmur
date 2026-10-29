using System.Runtime.InteropServices;
using MediatR;
using Murmur.Common;
using Serilog;

namespace Murmur.Interop;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class ApiBridge(IMediator mediator, ApiRegistry apiRegistry)
{
    public async Task<string> InvokeMethod(string serviceName, string methodName, string jsonParams)
    {
        IpcLogger.LogRequest(serviceName, methodName, jsonParams);
        try
        {
            var requestType = apiRegistry.GetRequestType(serviceName, methodName);
            if (requestType == null)
                return JsonUtils.Serialize(new { success = false, error = $"Unknown API: {serviceName}.{methodName}" });

            var request = JsonUtils.Deserialize(jsonParams, requestType);
            if (request == null)
                return JsonUtils.Serialize(new { success = false, error = $"Invalid params for {serviceName}.{methodName}" });

            var result = await Task.Run(() => mediator.Send(request)).ConfigureAwait(false);
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
