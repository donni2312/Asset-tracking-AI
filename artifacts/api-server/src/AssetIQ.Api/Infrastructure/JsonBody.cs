using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AssetIQ.Api.Infrastructure;

/// <summary>A field that may be absent from a JSON body, as opposed to present with a null value.</summary>
public readonly record struct Optional<T>(bool IsSet, T Value);

/// <summary>
/// Reads a JSON request body field by field with the semantics of the contract's schemas
/// (lib/api-spec/openapi.yaml): unknown keys are ignored, a missing required field is "Required",
/// "optional" fields may be absent but not null, "nullable" fields may be absent or null.
/// Problems are collected and reported together as one 400.
/// </summary>
public sealed partial class JsonBody
{
    private readonly JsonElement _root;
    private readonly bool _isObject;
    private readonly List<string> _issues = [];

    private JsonBody(JsonElement root)
    {
        _root = root;
        _isObject = root.ValueKind == JsonValueKind.Object;
    }

    public static JsonBody From(JsonElement? body)
    {
        var reader = new JsonBody(body ?? default);
        if (!reader._isObject)
        {
            reader._issues.Add($"Expected a JSON object body, received {Describe(body)}");
        }
        return reader;
    }

    public IReadOnlyList<string> Issues => _issues;

    public bool IsValid => _issues.Count == 0;

    /// <summary>Whether the body contains <paramref name="name"/> at all (null counts as present).</summary>
    public bool Has(string name) => TryGet(name, out _);

    public void ThrowIfInvalid()
    {
        if (_issues.Count > 0) throw new ApiException(StatusCodes.Status400BadRequest, string.Join("; ", _issues));
    }

    // ── strings ────────────────────────────────────────────────────────────────

    public string RequiredString(string name, int minLength = 0)
    {
        if (!TryGet(name, out var value))
        {
            Issue(name, "Required");
            return "";
        }
        return ReadString(name, value, minLength) ?? "";
    }

    public string? OptionalString(string name)
    {
        if (!TryGet(name, out var value)) return null;
        return ReadString(name, value, 0);
    }

    public Optional<string?> NullableString(string name)
    {
        if (!TryGet(name, out var value)) return default;
        if (value.ValueKind == JsonValueKind.Null) return new(true, null);
        return new(true, ReadString(name, value, 0));
    }

    public string RequiredEnum(string name, IReadOnlyList<string> allowed)
    {
        if (!TryGet(name, out var value))
        {
            Issue(name, "Required");
            return "";
        }
        return ReadEnum(name, value, allowed) ?? "";
    }

    public string? OptionalEnum(string name, IReadOnlyList<string> allowed)
    {
        if (!TryGet(name, out var value)) return null;
        return ReadEnum(name, value, allowed);
    }

    // ── numbers ────────────────────────────────────────────────────────────────

    public int RequiredInt(string name)
    {
        if (!TryGet(name, out var value))
        {
            Issue(name, "Required");
            return 0;
        }
        return ReadInt(name, value) ?? 0;
    }

    public int? OptionalInt(string name)
    {
        if (!TryGet(name, out var value)) return null;
        return ReadInt(name, value);
    }

    public Optional<int?> NullableInt(string name)
    {
        if (!TryGet(name, out var value)) return default;
        if (value.ValueKind == JsonValueKind.Null) return new(true, null);
        return new(true, ReadInt(name, value));
    }

    public decimal? OptionalNumber(string name)
    {
        if (!TryGet(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.Number)
        {
            Issue(name, $"Expected number, received {Describe(value)}");
            return null;
        }
        if (value.TryGetDecimal(out var exact)) return exact;
        if (value.TryGetDouble(out var approximate) && double.IsFinite(approximate)
            && Math.Abs(approximate) < (double)decimal.MaxValue)
        {
            return (decimal)approximate;
        }
        Issue(name, "Number is out of range");
        return null;
    }

    // ── booleans and maps ──────────────────────────────────────────────────────

    public bool RequiredBool(string name)
    {
        if (!TryGet(name, out var value))
        {
            Issue(name, "Required");
            return false;
        }
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        Issue(name, $"Expected boolean, received {Describe(value)}");
        return false;
    }

    /// <summary>
    /// An object of template values (<c>{ "ram": 16, "make": "Dell", "mac_address": null }</c>): strings and
    /// numbers are read as text, null means "clear". Absent → not set.
    /// </summary>
    public Optional<Dictionary<string, string?>> OptionalValueMap(string name)
    {
        if (!TryGet(name, out var value)) return default;
        if (value.ValueKind != JsonValueKind.Object)
        {
            Issue(name, $"Expected object, received {Describe(value)}");
            return new(true, []);
        }
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String: map[property.Name] = property.Value.GetString(); break;
                case JsonValueKind.Number: map[property.Name] = property.Value.GetRawText(); break;
                case JsonValueKind.Null: map[property.Name] = null; break;
                default: Issue($"{name}.{property.Name}", $"Expected string or number, received {Describe(property.Value)}"); break;
            }
        }
        return new(true, map);
    }

    /// <summary>A calendar date as YYYY-MM-DD text (the text-typed date columns); absent → null.</summary>
    public Optional<string?> NullableDay(string name)
    {
        if (!TryGet(name, out var value)) return default;
        if (value.ValueKind == JsonValueKind.Null) return new(true, null);
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : null;
        if (text is null || !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            Issue(name, "Expected a date (YYYY-MM-DD)");
            return new(true, null);
        }
        return new(true, text);
    }

    public Optional<decimal?> NullableNumber(string name)
    {
        if (!TryGet(name, out var value)) return default;
        if (value.ValueKind == JsonValueKind.Null) return new(true, null);
        return new(true, OptionalNumber(name));
    }

    // ── dates ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A date or date-time, coerced the way JavaScript's <c>new Date(value)</c> reads it: a bare
    /// YYYY-MM-DD is midnight UTC, a date-time without an offset is server-local time, a number is
    /// epoch milliseconds. Absent or null means "not given". Millisecond precision, UTC.
    /// </summary>
    public DateTime? OptionalDate(string name)
    {
        if (!TryGet(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        DateTime? parsed = value.ValueKind switch
        {
            JsonValueKind.String => ParseDateString(value.GetString()!),
            JsonValueKind.Number when value.TryGetDouble(out var ms) => FromEpochMilliseconds(ms),
            _ => null,
        };
        if (parsed is null) Issue(name, "Invalid date");
        return parsed;
    }

    internal static DateTime? ParseDateString(string text)
    {
        var s = text.Trim();
        if (DateOnlyPattern().IsMatch(s))
        {
            var formats = new[] { "yyyy-MM-dd", "yyyy-MM", "yyyy" };
            return DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var day)
                ? DateTime.SpecifyKind(day, DateTimeKind.Utc)
                : null;
        }
        var hasOffset = OffsetSuffixPattern().IsMatch(s);
        var style = hasOffset ? DateTimeStyles.AdjustToUniversal : DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal;
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, style, out var instant)) return null;
        return AppClock.TruncateToMilliseconds(DateTime.SpecifyKind(instant, DateTimeKind.Utc));
    }

    private static DateTime? FromEpochMilliseconds(double ms)
    {
        if (!double.IsFinite(ms)) return null;
        var whole = Math.Truncate(ms);
        if (whole < -62135596800000d || whole > 253402300799999d) return null;
        return DateTimeOffset.FromUnixTimeMilliseconds((long)whole).UtcDateTime;
    }

    [GeneratedRegex(@"^[0-9]{4}(-[0-9]{2}(-[0-9]{2})?)?$")]
    private static partial Regex DateOnlyPattern();

    [GeneratedRegex(@"(Z|z|[+-][0-9]{2}(:?[0-9]{2})?)$")]
    private static partial Regex OffsetSuffixPattern();

    // ── internals ──────────────────────────────────────────────────────────────

    private bool TryGet(string name, out JsonElement value)
    {
        if (_isObject && _root.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }

    private string? ReadString(string name, JsonElement value, int minLength)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            Issue(name, $"Expected string, received {Describe(value)}");
            return null;
        }
        var text = value.GetString()!;
        if (text.Length < minLength) Issue(name, $"String must contain at least {minLength} character(s)");
        return text;
    }

    private string? ReadEnum(string name, JsonElement value, IReadOnlyList<string> allowed)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (text is not null && allowed.Contains(text, StringComparer.Ordinal)) return text;
        var expected = string.Join(" | ", allowed.Select(a => $"'{a}'"));
        var received = text is not null ? $"'{text}'" : Describe(value);
        Issue(name, $"Invalid enum value. Expected {expected}, received {received}");
        return null;
    }

    private int? ReadInt(string name, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            Issue(name, $"Expected number, received {Describe(value)}");
            return null;
        }
        if (value.TryGetInt32(out var exact)) return exact;
        if (value.TryGetDouble(out var d) && Math.Floor(d) == d)
        {
            if (d >= int.MinValue && d <= int.MaxValue) return (int)d;
            Issue(name, "Number is out of range");
            return null;
        }
        Issue(name, "Expected integer, received float");
        return null;
    }

    private void Issue(string name, string message) => _issues.Add($"{name}: {message}");

    private static string Describe(JsonElement? value) => value?.ValueKind switch
    {
        null or JsonValueKind.Undefined => "nothing",
        JsonValueKind.Null => "null",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Array => "array",
        _ => "object",
    };
}
