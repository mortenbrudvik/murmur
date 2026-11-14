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

    // Strict about enums: the bridge's requests come from a web page, and a number would otherwise become any value of
    // the enum, defined or not. Files are always written with names, so reading them is unaffected.
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new DefinedEnumConverter() }
    };

    public static string Serialize(object? dataObj) =>
        JsonSerializer.Serialize(dataObj, SerializeOptions);

    public static object? Deserialize(string jsonParams, Type requestType) =>
        JsonSerializer.Deserialize(jsonParams, requestType, DeserializeOptions);
}
