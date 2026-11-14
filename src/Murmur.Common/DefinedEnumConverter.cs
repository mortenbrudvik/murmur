using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmur.Common;

/// <summary>
/// Reads enums only from their camelCase names and writes them the same way. A number, or a name or combination that
/// the enum does not define, is a <see cref="JsonException"/>: the plain <see cref="JsonStringEnumConverter"/> would
/// accept any integer, including one no member has. A <see cref="FlagsAttribute"/> enum accepts any combination of its
/// defined flags.
/// </summary>
public sealed class DefinedEnumConverter : JsonConverterFactory
{
    private readonly JsonStringEnumConverter _names = new(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var inner = _names.CreateConverter(typeToConvert, options);
        return (JsonConverter?)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert), inner);
    }

    private sealed class Converter<TEnum>(JsonConverter inner) : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        private readonly JsonConverter<TEnum> _inner = (JsonConverter<TEnum>)inner;

        private static readonly bool IsFlags = typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false);

        private static readonly ulong AllFlags =
            Enum.GetValues<TEnum>().Aggregate(0UL, (all, value) => all | Convert.ToUInt64(value, null));

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected the name of a {typeof(TEnum).Name} value, not {reader.TokenType}.");

            // The inner converter reads "a, b" as a combination even for an enum that is not [Flags].
            if (!IsFlags && reader.GetString()!.Contains(',', StringComparison.Ordinal))
                throw new JsonException($"Expected the name of one {typeof(TEnum).Name} value.");

            var value = _inner.Read(ref reader, typeToConvert, options);
            if (!IsDefinedValue(value))
                throw new JsonException($"{value} is not a {typeof(TEnum).Name} value.");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            _inner.Write(writer, value, options);

        private static bool IsDefinedValue(TEnum value) =>
            IsFlags
                ? (Convert.ToUInt64(value, null) & ~AllFlags) == 0
                : Enum.IsDefined(value);
    }
}
