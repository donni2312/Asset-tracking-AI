using System.Globalization;
using System.Text.RegularExpressions;

namespace AssetIQ.Api.Infrastructure;

/// <summary>JavaScript's <c>Number(text)</c>: "" → 0, " 7 " → 7, "7.0" → 7, "1e3" → 1000, junk → NaN.</summary>
public static partial class JsNumber
{
    public static double Parse(string? text)
    {
        if (text is null) return double.NaN;
        var s = text.Trim();
        if (s.Length == 0) return 0;
        if (s is "Infinity" or "+Infinity") return double.PositiveInfinity;
        if (s == "-Infinity") return double.NegativeInfinity;
        if (s.Length > 2 && s[0] == '0' && char.ToLowerInvariant(s[1]) is 'x' or 'o' or 'b')
        {
            var radix = char.ToLowerInvariant(s[1]) switch { 'x' => 16, 'o' => 8, _ => 2 };
            try
            {
                return Convert.ToInt64(s[2..], radix);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
            {
                return double.NaN;
            }
        }
        return DecimalLiteral().IsMatch(s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : double.NaN;
    }

    public static bool IsInteger(double value) => double.IsFinite(value) && Math.Floor(value) == value;

    [GeneratedRegex(@"^[+-]?([0-9]+\.?[0-9]*|\.[0-9]+)([eE][+-]?[0-9]+)?$")]
    private static partial Regex DecimalLiteral();
}

public static class RequestValues
{
    /// <summary>A positive integer route id, or a 400 "Invalid id: …".</summary>
    public static int Id(string? raw)
    {
        var n = JsNumber.Parse(raw);
        if (!JsNumber.IsInteger(n) || n <= 0 || n > int.MaxValue)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, $"Invalid id: {raw}");
        }
        return (int)n;
    }

    /// <summary>An integer query filter; absent, empty or not an integer means "no filter".</summary>
    public static int? OptionalInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>A text query filter; absent or empty means "no filter".</summary>
    public static string? OptionalText(string? raw) => string.IsNullOrEmpty(raw) ? null : raw;

    /// <summary>A text filter restricted to <paramref name="allowed"/>; anything else means "no filter".</summary>
    public static string? OptionalChoice(string? raw, IReadOnlyList<string> allowed) =>
        raw is not null && allowed.Contains(raw, StringComparer.Ordinal) ? raw : null;

    /// <summary>Phase 4c: an optional array of integers in a JSON body (non-integers are a 400).</summary>
    public static List<int>? IntList(System.Text.Json.JsonElement? body, string name)
    {
        if (body is not { ValueKind: System.Text.Json.JsonValueKind.Object } b || !b.TryGetProperty(name, out var arr)
            || arr.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
        if (arr.ValueKind != System.Text.Json.JsonValueKind.Array) throw new ApiException(400, $"{name}: Expected array");
        return arr.EnumerateArray().Select(x => x.ValueKind == System.Text.Json.JsonValueKind.Number && x.TryGetInt32(out var v) ? v
            : throw new ApiException(400, $"{name}: Expected integers")).ToList();
    }

    /// <summary>Phase 4c: an optional array of strings in a JSON body.</summary>
    public static List<string>? StringList(System.Text.Json.JsonElement? body, string name)
    {
        if (body is not { ValueKind: System.Text.Json.JsonValueKind.Object } b || !b.TryGetProperty(name, out var arr)
            || arr.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
        if (arr.ValueKind != System.Text.Json.JsonValueKind.Array) throw new ApiException(400, $"{name}: Expected array");
        return arr.EnumerateArray().Select(x => x.ValueKind == System.Text.Json.JsonValueKind.String ? x.GetString()!
            : throw new ApiException(400, $"{name}: Expected strings")).ToList();
    }
}
