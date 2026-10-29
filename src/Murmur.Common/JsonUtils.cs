using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmur.Common;

public static class JsonUtils
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(object? dataObj) =>
        JsonSerializer.Serialize(dataObj, SerializeOptions);

    public static object? Deserialize(string jsonParams, Type requestType) =>
        JsonSerializer.Deserialize(jsonParams, requestType, DeserializeOptions);
}
