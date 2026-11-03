using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EmpleadoDelMes.Api.Serialization;

// Applied only to campaign scheduling input. Never infer an offset from the server's time zone.
public sealed class ExplicitOffsetDateTimeConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Indica una fecha con desplazamiento horario.");
        var value = reader.GetString()!;
        if (!Regex.IsMatch(value, @"T.*(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant) ||
            !reader.TryGetDateTimeOffset(out var date))
            throw new JsonException("Indica una fecha ISO 8601 con Z o un desplazamiento como -06:00.");
        return date;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
