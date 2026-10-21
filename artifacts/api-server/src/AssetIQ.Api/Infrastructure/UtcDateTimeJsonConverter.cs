using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetIQ.Api.Infrastructure;

/// <summary>
/// Timestamps on the wire are UTC with millisecond precision, exactly like JavaScript's
/// <c>Date.toISOString()</c>: <c>2026-10-14T08:20:00.000Z</c>.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            throw new JsonException($"Invalid date-time: {text}");
        }
        return parsed.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToIsoString(value));

    /// <summary>Unspecified kinds are UTC: that is what every timestamp column holds.</summary>
    public static string ToIsoString(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return utc.ToString(Format, CultureInfo.InvariantCulture);
    }
}
