using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SplitMoneyTg.Api;

public sealed class LongAsStringJsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var numericValue)) return numericValue;
        if (reader.TokenType == JsonTokenType.String &&
            long.TryParse(reader.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var stringValue))
            return stringValue;
        throw new JsonException("Int64 values must be decimal JSON strings or numbers.");
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
