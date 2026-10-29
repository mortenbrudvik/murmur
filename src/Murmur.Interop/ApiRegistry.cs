using System.Collections.Concurrent;
using MediatR;

namespace Murmur.Interop;

public class ApiRegistry
{
    private readonly ConcurrentDictionary<string, Type> _requests = new();

    public void Register<TRequest>(string serviceName, string methodName) where TRequest : IBaseRequest
    {
        _requests[$"{serviceName}.{methodName}"] = typeof(TRequest);
    }

    public Type? GetRequestType(string serviceName, string methodName)
    {
        return _requests.GetValueOrDefault($"{serviceName}.{methodName}");
    }
}
