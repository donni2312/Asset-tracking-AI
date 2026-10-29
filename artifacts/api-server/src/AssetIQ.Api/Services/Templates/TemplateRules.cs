using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;

namespace AssetIQ.Api.Services.Templates;

public sealed record TemplateField(int Id, string Key, string Label, string Type, string? Unit, bool Required, IReadOnlyList<string>? AllowedValues);

/// <summary>A category template as the rules see it (loaded from asset_categories + category_template_fields).</summary>
public sealed record Template(
    int CategoryId, string Code, string Name, bool IsIt, IReadOnlyList<string> SubCategories,
    string DescriptionPattern, string? IdentifierFieldKey, IReadOnlyList<TemplateField> Fields)
{
    /// <summary>Template field keys stored in their own asset columns rather than as attribute values.</summary>
    public const string MacAddressKey = "mac_address";

    public TemplateField? Field(string key) => Fields.FirstOrDefault(f => f.Key == key);

    public TemplateField? IdentifierField => IdentifierFieldKey is null ? null : Field(IdentifierFieldKey);

    public static Template From(AssetCategory category) => new(
        category.Id, category.Code, category.Name, category.IsIt,
        JsonSerializer.Deserialize<List<string>>(category.SubCategories) ?? [],
        category.DescriptionPattern, category.IdentifierFieldKey,
        category.Fields.OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
            .Select(f => new TemplateField(f.Id, f.Key, f.Label, f.Type, f.Unit, f.Required,
                f.AllowedValues is null ? null : JsonSerializer.Deserialize<List<string>>(f.AllowedValues)))
            .ToList());
}

/// <summary>A validated template value: the text as stored, plus the number for number/integer fields.</summary>
public sealed record FieldValue(string Text, decimal? Number);

public sealed record TemplateValidation(IReadOnlyList<string> Errors, IReadOnlyDictionary<string, FieldValue> Values)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// AU-01 / GI-02 / AM-01 rules for template values. Pure: no database, so every rule is unit tested.
/// </summary>
public static partial class TemplateRules
{
    /// <summary>
    /// Checks <paramref name="values"/> (field key → value as entered; null or blank = not given) against the
    /// template: every key must belong to it, every required field must be filled (the identifier field may be
    /// left out only when the asset is recorded as "No serial"), and values must parse as the field's type.
    /// </summary>
    public static TemplateValidation Validate(Template template, string? subCategory, IReadOnlyDictionary<string, string?> values, bool noSerial)
    {
        var errors = new List<string>();
        var clean = new Dictionary<string, FieldValue>(StringComparer.Ordinal);

        if (template.SubCategories.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(subCategory))
                errors.Add($"subCategory: Required for {template.Name} ({string.Join(", ", template.SubCategories)})");
            else if (!template.SubCategories.Contains(subCategory.Trim(), StringComparer.Ordinal))
                errors.Add($"subCategory: '{subCategory}' is not a sub-category of {template.Name} ({string.Join(", ", template.SubCategories)})");
        }

        foreach (var key in values.Keys.Where(k => template.Field(k) is null).Order(StringComparer.Ordinal))
        {
            errors.Add($"attributes.{key}: is not a field of {template.Name}");
        }

        foreach (var field in template.Fields)
        {
            values.TryGetValue(field.Key, out var raw);
            var text = raw?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                var excused = noSerial && field.Key == template.IdentifierFieldKey;
                if (field.Required && !excused) errors.Add($"attributes.{field.Key}: {field.Label} is required for {template.Name}");
                continue;
            }
            var parsed = Parse(field, text, out var problem);
            if (parsed is null) errors.Add($"attributes.{field.Key}: {problem}");
            else clean[field.Key] = parsed;
        }
        return new TemplateValidation(errors, clean);
    }

    private static FieldValue? Parse(TemplateField field, string text, out string problem)
    {
        problem = "";
        switch (field.Type)
        {
            case TemplateFieldTypes.Integer:
                if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)) return new(whole.ToString(CultureInfo.InvariantCulture), whole);
                problem = $"{field.Label} must be a whole number";
                return null;
            case TemplateFieldTypes.Number:
                if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                    return new(number.ToString(CultureInfo.InvariantCulture), number);
                problem = $"{field.Label} must be a number";
                return null;
            case TemplateFieldTypes.Date:
                if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return new(text, null);
                problem = $"{field.Label} must be a date (YYYY-MM-DD)";
                return null;
            case TemplateFieldTypes.List:
                var allowed = field.AllowedValues ?? [];
                var match = allowed.FirstOrDefault(v => string.Equals(v, text, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return new(match, null);
                problem = $"{field.Label} must be one of {string.Join(", ", allowed)}";
                return null;
            default:
                return new(text, null);
        }
    }

    /// <summary>
    /// The description (AU-01) from the template's pattern. Tokens are {SubCategory}, {Category} and each
    /// field key in PascalCase ({Ram} for "ram", {RegistrationNumber} for "registration_number"); a field
    /// with a unit renders as "16 GB". Tokens without a value are dropped together with their separator.
    /// </summary>
    public static string BuildDescription(Template template, string? subCategory, IReadOnlyDictionary<string, FieldValue> values)
    {
        var text = Token().Replace(template.DescriptionPattern, m =>
        {
            var token = m.Groups[1].Value;
            if (token == "SubCategory") return subCategory?.Trim() ?? "";
            if (token == "Category") return template.Name;
            var field = template.Fields.FirstOrDefault(f => PascalCase(f.Key) == token);
            if (field is null || !values.TryGetValue(field.Key, out var value)) return "";
            return field.Unit is null ? value.Text : $"{value.Text} {field.Unit}";
        });
        return Tidy(text);
    }

    public static string PascalCase(string key)
    {
        var sb = new StringBuilder(key.Length);
        var upper = true;
        foreach (var c in key)
        {
            if (c == '_') { upper = true; continue; }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        return sb.ToString();
    }

    /// <summary>Removes the separators left behind by empty tokens: ", ," → ",", leading/trailing "–" or ",", double spaces.</summary>
    private static string Tidy(string text)
    {
        var s = Spaces().Replace(text, " ");
        s = EmptyListItem().Replace(s, ",");
        s = EmptyDash().Replace(s, " – ");
        s = s.Trim().Trim(',', '–').Trim();
        s = TrailingSeparator().Replace(s, "");
        s = LeadingSeparator().Replace(s, "");
        return Spaces().Replace(s, " ").Trim();
    }

    [GeneratedRegex(@"\{([A-Za-z0-9]+)\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*,(\s*,)+")]
    private static partial Regex EmptyListItem();

    [GeneratedRegex(@"\s*–\s*(,\s*)+")]
    private static partial Regex EmptyDash();

    [GeneratedRegex(@"[\s,–]+$")]
    private static partial Regex TrailingSeparator();

    [GeneratedRegex(@"^[\s,–]+")]
    private static partial Regex LeadingSeparator();

    /// <summary>Serials, VINs and IMEIs are stored trimmed and upper-cased, so "5cg4182kxl" and "5CG4182KXL" collide.</summary>
    // ASSUMPTION: manufacturer identifiers are case-insensitive; upper-casing makes the Group-wide unique
    // constraint catch duplicates typed in a different case.
    public static string NormaliseIdentifier(string value) => value.Trim().ToUpperInvariant();

    /// <summary>MAC addresses as AA:BB:CC:DD:EE:FF whatever separators were typed; null when it is not a MAC.</summary>
    public static string? NormaliseMac(string value)
    {
        var hex = new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (hex.Length != 12 || MacCharacters().IsMatch(value) is false) return null;
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    [GeneratedRegex(@"^[0-9A-Fa-f:\-.\s]+$")]
    private static partial Regex MacCharacters();
}
