using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using AssetIQ.Api.Services.Templates;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

/// <summary>
/// Category templates (AU-01, GI-02, AM-01, GF-03), Group settings, auditor accounts (AU-12) and the access
/// log. Per the Permissions matrix: Group Finance owns categories and depreciation policy, Group IT the fields
/// of IT categories, the SBU Admin Manager those of non-IT categories. All changes are audit-logged.
/// </summary>
public sealed class AdministrationController(AssetIqDbContext db, AppClock clock, AuditTrail audit, PasswordHasher passwords) : ApiControllerBase
{
    // ── categories ─────────────────────────────────────────────────────────────

    [HttpGet("categories")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to list categories")]
    public async Task<List<AssetCategoryDto>> Categories([FromQuery] string? includeInactive)
    {
        var all = includeInactive == "true";
        var rows = await db.AssetCategories.AsNoTracking().Include(c => c.Fields).Where(c => all || c.Active).OrderBy(c => c.Name).ToListAsync(Aborted);
        return rows.Select(ToDto).ToList();
    }

    [HttpGet("categories/{id}")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to get the category")]
    public async Task<AssetCategoryDto> Category(string id) => ToDto(await LoadCategoryAsync(RequestValues.Id(id)));

    [HttpPost("categories")]
    [RequireRole(Roles.CategoryOwners)]
    [FailureMessage("Failed to create the category")]
    public async Task<IActionResult> CreateCategory([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        if (!AccessPolicy.CanManageCategories(Caller)) throw AccessPolicy.Deny(Caller, "create categories (Group Finance or the System Administrator)");
        var input = JsonBody.From(body);
        var code = input.RequiredString("code", minLength: 2).Trim().ToUpperInvariant();
        var name = input.RequiredString("name", minLength: 2).Trim();
        var isIt = input.RequiredBool("isIt");
        var subCategories = ReadList(input, body, "subCategories");
        var pattern = input.OptionalString("descriptionPattern") ?? "{SubCategory} – {Make} {Model}";
        var identifier = input.OptionalString("identifierFieldKey");
        var method = input.RequiredEnum("depreciationMethod", DepreciationMethods.All);
        var life = input.OptionalInt("usefulLifeMonths");
        var rate = input.OptionalNumber("annualRate");
        var residualPercent = input.OptionalNumber("defaultResidualPercent") ?? 0m;
        input.ThrowIfInvalid();
        CheckPolicy(method, life, rate);
        CheckResidual(residualPercent);
        if (await db.AssetCategories.AnyAsync(c => c.Code == code || c.Name == name, Aborted))
            throw new ApiException(StatusCodes.Status409Conflict, "A category with this code or name already exists");

        var now = clock.UtcNow;
        var category = new AssetCategory
        {
            Code = code, Name = name, IsIt = isIt, SubCategories = JsonSerializer.Serialize(subCategories), DescriptionPattern = pattern,
            IdentifierFieldKey = identifier, DepreciationMethod = method, UsefulLifeMonths = life, AnnualRate = rate,
            DefaultResidualPercent = residualPercent, CreatedAt = now, UpdatedAt = now,
        };
        // Every category starts with the fields AU-01 makes mandatory; field owners add the rest.
        category.Fields.Add(new CategoryTemplateField { Key = "make", Label = "Make", Type = "text", Required = true, SortOrder = 1, CreatedAt = now, UpdatedAt = now });
        category.Fields.Add(new CategoryTemplateField { Key = "model", Label = "Model", Type = "text", Required = true, SortOrder = 2, CreatedAt = now, UpdatedAt = now });
        db.AssetCategories.Add(category);
        await db.SaveChangesAsync(Aborted);
        audit.Record(Caller, "category", category.Id, null, "created", "name", null, name, $"{(isIt ? "IT" : "non-IT")}, {method}");
        await db.SaveChangesAsync(Aborted);
        return Status201(ToDto(await LoadCategoryAsync(category.Id)));
    }

    /// <summary>
    /// Group Finance: name, sub-categories, depreciation policy, active. Field owners (Group IT / SBU Admin
    /// Manager): description pattern and identifier field.
    /// </summary>
    [HttpPatch("categories/{id}")]
    [RequireRole(Roles.CategoryOwners)]
    [FailureMessage("Failed to update the category")]
    public async Task<AssetCategoryDto> UpdateCategory(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var category = await db.AssetCategories.Include(c => c.Fields).FirstOrDefaultAsync(c => c.Id == RequestValues.Id(id), Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Category not found");
        var input = JsonBody.From(body);
        var name = input.OptionalString("name");
        var subCategories = input.Has("subCategories") ? ReadList(input, body, "subCategories") : null;
        var method = input.OptionalEnum("depreciationMethod", DepreciationMethods.All);
        var life = input.NullableInt("usefulLifeMonths");
        var rate = input.NullableNumber("annualRate");
        var residualPercent = input.OptionalNumber("defaultResidualPercent");
        var active = input.Has("active") ? input.RequiredBool("active") : (bool?)null;
        var pattern = input.OptionalString("descriptionPattern");
        var identifier = input.NullableString("identifierFieldKey");
        var reason = input.OptionalString("reason");
        input.ThrowIfInvalid();

        var policyChange = name is not null || subCategories is not null || method is not null || life.IsSet || rate.IsSet || active is not null
            || residualPercent is not null;
        var templateChange = pattern is not null || identifier.IsSet;
        if (policyChange && !AccessPolicy.CanManageCategories(Caller)) throw AccessPolicy.Deny(Caller, "change categories or depreciation rates (Group Finance or the System Administrator)");
        if (templateChange && !AccessPolicy.CanManageFields(Caller, category.IsIt))
            throw AccessPolicy.Deny(Caller, category.IsIt ? "change IT templates (Group IT only)" : "change non-IT templates (SBU Admin Manager only)");
        if (identifier is { IsSet: true, Value: { } key } && category.Fields.All(f => f.Key != key))
            throw new ApiException(StatusCodes.Status400BadRequest, $"identifierFieldKey: {key} is not a field of {category.Name}");
        CheckPolicy(method ?? category.DepreciationMethod, life.IsSet ? life.Value : category.UsefulLifeMonths, rate.IsSet ? rate.Value : category.AnnualRate);
        if (residualPercent is { } percent) CheckResidual(percent);

        void Set<T>(string field, T before, T after, Action<T> apply)
        {
            if (audit.RecordChange(Caller, "category", category.Id, null, field, before, after, reason)) apply(after);
        }
        if (name is not null) Set("name", category.Name, name.Trim(), v => category.Name = v);
        if (subCategories is not null) Set("subCategories", category.SubCategories, JsonSerializer.Serialize(subCategories), v => category.SubCategories = v);
        if (method is not null) Set("depreciationMethod", category.DepreciationMethod, method, v => category.DepreciationMethod = v);
        if (life.IsSet) Set("usefulLifeMonths", category.UsefulLifeMonths, life.Value, v => category.UsefulLifeMonths = v);
        if (rate.IsSet) Set("annualRate", category.AnnualRate, rate.Value, v => category.AnnualRate = v);
        if (residualPercent is not null) Set("defaultResidualPercent", category.DefaultResidualPercent, residualPercent.Value, v => category.DefaultResidualPercent = v);
        if (active is not null) Set("active", category.Active, active.Value, v => category.Active = v);
        if (pattern is not null) Set("descriptionPattern", category.DescriptionPattern, pattern, v => category.DescriptionPattern = v);
        if (identifier.IsSet) Set("identifierFieldKey", category.IdentifierFieldKey, identifier.Value, v => category.IdentifierFieldKey = v);
        category.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(Aborted);
        return ToDto(await LoadCategoryAsync(category.Id));
    }

    [HttpPost("categories/{id}/fields")]
    [RequireRole(Roles.FieldOwners)]
    [FailureMessage("Failed to add the field")]
    public async Task<IActionResult> AddField(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var category = await LoadCategoryAsync(RequestValues.Id(id));
        if (!AccessPolicy.CanManageFields(Caller, category.IsIt))
            throw AccessPolicy.Deny(Caller, category.IsIt ? "change IT fields (Group IT only)" : "change non-IT fields (SBU Admin Manager only)");
        var input = JsonBody.From(body);
        var key = input.RequiredString("key", minLength: 2).Trim();
        var field = ReadField(input, body, key, category.Fields.Count + 1);
        input.ThrowIfInvalid();
        if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z][a-z0-9_]*$")) throw new ApiException(StatusCodes.Status400BadRequest, "key: lower-case letters, digits and _ only");
        if (category.Fields.Any(f => f.Key == key)) throw new ApiException(StatusCodes.Status409Conflict, $"{category.Name} already has a field {key}");
        field.CategoryId = category.Id;
        db.CategoryTemplateFields.Add(field);
        audit.Record(Caller, "category_field", category.Id, null, "field_added", key, null, $"{field.Label} ({field.Type}{(field.Required ? ", required" : "")})");
        await db.SaveChangesAsync(Aborted);
        return Status201(ToDto(await LoadCategoryAsync(category.Id)));
    }

    [HttpPatch("category-fields/{id}")]
    [RequireRole(Roles.FieldOwners)]
    [FailureMessage("Failed to update the field")]
    public async Task<AssetCategoryDto> UpdateField(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var field = await db.CategoryTemplateFields.FirstOrDefaultAsync(f => f.Id == RequestValues.Id(id), Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Field not found");
        var category = await LoadCategoryAsync(field.CategoryId);
        if (!AccessPolicy.CanManageFields(Caller, category.IsIt))
            throw AccessPolicy.Deny(Caller, category.IsIt ? "change IT fields (Group IT only)" : "change non-IT fields (SBU Admin Manager only)");
        var input = JsonBody.From(body);
        var label = input.OptionalString("label");
        var required = input.Has("required") ? input.RequiredBool("required") : (bool?)null;
        var unit = input.NullableString("unit");
        var allowed = input.Has("allowedValues") ? ReadList(input, body, "allowedValues") : null;
        var sort = input.OptionalInt("sortOrder");
        input.ThrowIfInvalid();
        if (required == false && field.Key is "make" or "model") throw new ApiException(StatusCodes.Status400BadRequest, "required: make and model are mandatory for every asset (AU-01)");

        void Set<T>(string name, T before, T after, Action<T> apply)
        {
            if (audit.RecordChange(Caller, "category_field", field.Id, null, $"{category.Code}.{field.Key}.{name}", before, after)) apply(after);
        }
        if (label is not null) Set("label", field.Label, label, v => field.Label = v);
        if (required is not null) Set("required", field.Required, required.Value, v => field.Required = v);
        if (unit.IsSet) Set("unit", field.Unit, unit.Value, v => field.Unit = v);
        if (allowed is not null) Set("allowedValues", field.AllowedValues, JsonSerializer.Serialize(allowed), v => field.AllowedValues = v);
        if (sort is not null) Set("sortOrder", field.SortOrder, sort.Value, v => field.SortOrder = v);
        field.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(Aborted);
        return ToDto(await LoadCategoryAsync(category.Id));
    }

    private CategoryTemplateField ReadField(JsonBody input, JsonElement? body, string key, int sortOrder)
    {
        var now = clock.UtcNow;
        var type = input.RequiredEnum("type", TemplateFieldTypes.All);
        var allowed = input.Has("allowedValues") ? ReadList(input, body, "allowedValues") : null;
        return new CategoryTemplateField
        {
            Key = key, Label = input.RequiredString("label", minLength: 1), Type = type, Unit = input.OptionalString("unit"),
            Required = input.RequiredBool("required"), AllowedValues = allowed is null ? null : JsonSerializer.Serialize(allowed),
            SortOrder = input.OptionalInt("sortOrder") ?? sortOrder, CreatedAt = now, UpdatedAt = now,
        };
    }

    private static List<string> ReadList(JsonBody input, JsonElement? body, string name)
    {
        if (body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            && value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String))
        {
            return value.EnumerateArray().Select(v => v.GetString()!.Trim()).Where(v => v.Length > 0).Distinct().ToList();
        }
        if (input.Has(name)) throw new ApiException(StatusCodes.Status400BadRequest, $"{name}: Expected an array of strings");
        return [];
    }

    /// <summary>The proposed residual value, as a % of cost: 0 unless Finance decides otherwise (product owner, 29 Oct 2026).</summary>
    private static void CheckResidual(decimal percent)
    {
        if (percent is < 0 or >= 100 || decimal.Round(percent, 2) != percent)
            throw new ApiException(StatusCodes.Status400BadRequest, "defaultResidualPercent: a percentage from 0 to 99.99 (2 decimals)");
    }

    private static void CheckPolicy(string method, int? life, decimal? rate)
    {
        if (method == DepreciationMethods.StraightLine && life is not > 0) throw new ApiException(StatusCodes.Status400BadRequest, "usefulLifeMonths: required (> 0) for straight-line");
        if (method == DepreciationMethods.ReducingBalance && rate is not (> 0 and < 1)) throw new ApiException(StatusCodes.Status400BadRequest, "annualRate: required for reducing balance, between 0 and 1 (0.25 = 25%)");
    }

    private async Task<AssetCategory> LoadCategoryAsync(int id) =>
        await db.AssetCategories.AsNoTracking().Include(c => c.Fields).FirstOrDefaultAsync(c => c.Id == id, Aborted)
        ?? throw new ApiException(StatusCodes.Status404NotFound, "Category not found");

    public static AssetCategoryDto ToDto(AssetCategory c)
    {
        var template = Template.From(c);
        return new AssetCategoryDto
        {
            Id = c.Id, Code = c.Code, Name = c.Name, IsIt = c.IsIt, SubCategories = template.SubCategories,
            DescriptionPattern = c.DescriptionPattern, IdentifierFieldKey = c.IdentifierFieldKey, DepreciationMethod = c.DepreciationMethod,
            UsefulLifeMonths = c.UsefulLifeMonths, AnnualRate = c.AnnualRate, DefaultResidualPercent = c.DefaultResidualPercent, Active = c.Active,
            Fields = template.Fields.Select(f => new TemplateFieldDto
            {
                Id = f.Id, Key = f.Key, Label = f.Label, Type = f.Type, Unit = f.Unit, Required = f.Required, AllowedValues = f.AllowedValues,
                IsIdentifier = f.Key == c.IdentifierFieldKey, SortOrder = c.Fields.First(x => x.Id == f.Id).SortOrder,
            }).ToList(),
        };
    }

    // ── Group settings ─────────────────────────────────────────────────────────

    [HttpGet("settings")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to list settings")]
    public async Task<List<GroupSettingDto>> Settings() =>
        (await db.GroupSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(Aborted)).Select(ToDto).ToList();

    [HttpPatch("settings/{key}")]
    [RequireRole(Roles.SettingOwners)]
    [FailureMessage("Failed to update the setting")]
    public async Task<GroupSettingDto> UpdateSetting(string key, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var setting = await db.GroupSettings.FirstOrDefaultAsync(s => s.Key == key, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Setting not found");
        if (!AccessPolicy.CanEditSetting(Caller, setting.EditableBy)) throw AccessPolicy.Deny(Caller, $"change {key} (editable by {Roles.DisplayName(setting.EditableBy)})");
        if (body is not { ValueKind: JsonValueKind.Object } b || !b.TryGetProperty("value", out var value))
            throw new ApiException(StatusCodes.Status400BadRequest, "value: Required");
        var current = JsonDocument.Parse(setting.Value).RootElement;
        if (current.ValueKind != value.ValueKind && !(current.ValueKind is JsonValueKind.True or JsonValueKind.False && value.ValueKind is JsonValueKind.True or JsonValueKind.False))
            throw new ApiException(StatusCodes.Status400BadRequest, $"value: expected the same kind of value as before ({current.ValueKind.ToString().ToLowerInvariant()})");
        if (key == "depreciation_convention" && value.GetString() != "full_month_from_in_use")
            throw new ApiException(StatusCodes.Status400BadRequest, "value: only \"full_month_from_in_use\" is supported");
        audit.RecordChange(Caller, "setting", null, null, key, setting.Value, value.GetRawText(),
            b.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null);
        setting.Value = value.GetRawText();
        setting.UpdatedBy = Caller.Name;
        setting.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(Aborted);
        return ToDto(setting);
    }

    private static GroupSettingDto ToDto(GroupSetting s) => new()
    {
        Key = s.Key, Value = JsonDocument.Parse(s.Value).RootElement.Clone(), Description = s.Description,
        EditableBy = s.EditableBy, UpdatedBy = s.UpdatedBy, UpdatedAt = s.UpdatedAt,
    };

    // ── auditors (AU-12) and the access log ────────────────────────────────────

    [HttpGet("users/auditors")]
    [RequireRole(Roles.AuditorManagers)]
    [FailureMessage("Failed to list auditors")]
    public async Task<List<AccountDto>> Auditors()
    {
        if (!AccessPolicy.CanManageAuditors(Caller)) throw AccessPolicy.Deny(Caller, "manage auditor access (Group Finance only)");
        return await db.Users.AsNoTracking().Where(u => u.Role == Roles.Auditor).OrderBy(u => u.Username).Select(u => ToAccount(u)).ToListAsync(Aborted);
    }

    /// <summary>AU-12: a time-limited, read-only login for an internal or external auditor.</summary>
    [HttpPost("users/auditors")]
    [RequireRole(Roles.AuditorManagers)]
    [FailureMessage("Failed to create the auditor account")]
    public async Task<IActionResult> CreateAuditor([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        if (!AccessPolicy.CanManageAuditors(Caller)) throw AccessPolicy.Deny(Caller, "create auditor accounts (Group Finance only)");
        var input = JsonBody.From(body);
        var username = input.RequiredString("username", minLength: 3).Trim().ToLowerInvariant();
        var name = input.RequiredString("name", minLength: 2).Trim();
        var password = input.RequiredString("password", minLength: 10);
        var expires = input.OptionalDate("accessExpiresAt");
        if (!input.Has("accessExpiresAt")) input.RequiredString("accessExpiresAt");
        input.ThrowIfInvalid();
        if (expires is null || expires <= clock.UtcNow) throw new ApiException(StatusCodes.Status400BadRequest, "accessExpiresAt: must be in the future");
        if (await db.Users.AnyAsync(u => u.Username == username, Aborted)) throw new ApiException(StatusCodes.Status409Conflict, "That username is taken");

        var now = clock.UtcNow;
        var user = new UserAccount
        {
            Username = username, Name = name, Role = Roles.Auditor, PasswordHash = passwords.Hash(password),
            AccessExpiresAt = expires, Active = true, CreatedAt = now, UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(Aborted);
        audit.Record(Caller, "user", user.Id, null, "auditor_created", "accessExpiresAt", null, expires, $"Read-only auditor access for {name}");
        await db.SaveChangesAsync(Aborted);
        return Status201(ToAccount(user));
    }

    [HttpPatch("users/{id}/access")]
    [RequireRole(Roles.AuditorManagers)]
    [FailureMessage("Failed to change the access")]
    public async Task<AccountDto> UpdateAuditorAccess(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        if (!AccessPolicy.CanManageAuditors(Caller)) throw AccessPolicy.Deny(Caller, "change auditor access (Group Finance only)");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == RequestValues.Id(id) && u.Role == Roles.Auditor, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Auditor not found");
        var input = JsonBody.From(body);
        var expires = input.OptionalDate("accessExpiresAt");
        var active = input.Has("active") ? input.RequiredBool("active") : (bool?)null;
        input.ThrowIfInvalid();
        if (expires is not null && audit.RecordChange(Caller, "user", user.Id, null, "accessExpiresAt", user.AccessExpiresAt, expires)) user.AccessExpiresAt = expires;
        if (active is not null && audit.RecordChange(Caller, "user", user.Id, null, "active", user.Active, active.Value)) user.Active = active.Value;
        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(Aborted);
        return ToAccount(user);
    }

    /// <summary>AU-12: sign-ins and exports. Group Finance sees everyone's.</summary>
    [HttpGet("access-log")]
    [RequireRole(Roles.AccessLogReaders)]
    [FailureMessage("Failed to load the access log")]
    public async Task<List<AccessLogEntryDto>> AccessLogEntries([FromQuery] string? role)
    {
        if (!AccessPolicy.CanManageAuditors(Caller)) throw AccessPolicy.Deny(Caller, "see the access log (Group Finance only)");
        return await db.AccessLog.AsNoTracking().Where(e => role == null || e.Role == role).OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Take(500)
            .Select(e => new AccessLogEntryDto(e.Id, e.At, e.Username, e.Role, e.Action, e.Detail)).ToListAsync(Aborted);
    }

    private static AccountDto ToAccount(UserAccount u) => new()
    {
        Id = u.Id, Username = u.Username, Name = u.Name, Role = u.Role, SbuId = u.SbuId, EmployeeId = u.EmployeeId, AccessExpiresAt = u.AccessExpiresAt,
        Active = u.Active, LastLoginAt = u.LastLoginAt,
    };
}
