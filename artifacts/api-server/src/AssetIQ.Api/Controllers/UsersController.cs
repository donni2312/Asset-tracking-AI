using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

/// <summary>
/// Matrix "Manage users" (GI-08): Group IT manages every account except auditors (Group Finance, AU-12); SBU
/// IT manages the accounts of its own SBU and can only give SBU roles in that SBU. Nobody changes their own
/// role or disables themselves. Every change is audit-logged.
/// </summary>
public sealed class UsersController(AssetIqDbContext db, AppClock clock, PasswordHasher passwords, AuditTrail audit) : ApiControllerBase
{
    [HttpGet("roles")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to list roles")]
    public List<RoleDto> RoleList() =>
        Roles.All.Select(r => new RoleDto(r, Roles.DisplayName(r), Roles.IsSbuScoped(r), Roles.NeedEmployee.Contains(r))).ToList();

    [HttpGet("users")]
    [RequireRole(Roles.UserManagers)]
    [FailureMessage("Failed to list users")]
    public Task<List<UserAccountDto>> List([FromQuery] string? sbuId, [FromQuery] string? role)
    {
        var sbuFilter = RequestValues.OptionalInt(sbuId);
        var roleFilter = RequestValues.OptionalChoice(role, Roles.All);
        var query = db.Users.AsNoTracking();
        if (Caller.Role == Roles.SbuIt) query = query.Where(u => u.SbuId == Caller.SbuId);
        if (sbuFilter is not null) query = query.Where(u => u.SbuId == sbuFilter);
        if (roleFilter is not null) query = query.Where(u => u.Role == roleFilter);
        return Project(query.OrderBy(u => u.Username)).ToListAsync(Aborted);
    }

    [HttpPost("users")]
    [RequireRole(Roles.UserManagers)]
    [FailureMessage("Failed to create the user")]
    public async Task<IActionResult> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var username = input.RequiredString("username", minLength: 3).Trim().ToLowerInvariant();
        var name = input.RequiredString("name", minLength: 2).Trim();
        var password = input.RequiredString("password", minLength: 10);
        var role = input.RequiredEnum("role", Roles.All);
        // SBU IT may leave the SBU out: its own SBU is meant.
        var sbuId = input.NullableInt("sbuId").Value ?? (Caller.Role == Roles.SbuIt && Roles.IsSbuScoped(role) ? Caller.SbuId : null);
        var employeeId = input.NullableInt("employeeId").Value;
        input.ThrowIfInvalid();

        if (role == Roles.Auditor) throw AccessPolicy.Deny(Caller, "create auditor accounts (Group Finance does, AU-12)");
        if (!AccessPolicy.CanManageUser(Caller, role, sbuId)) throw AccessPolicy.Deny(Caller, $"create {Roles.DisplayName(role)} accounts{(sbuId is null ? "" : " for this SBU")}");
        await ValidateAssignmentAsync(role, sbuId, employeeId, null);
        if (await db.Users.AnyAsync(u => u.Username == username, Aborted)) throw new ApiException(StatusCodes.Status409Conflict, "That username is taken");

        var now = clock.UtcNow;
        var user = new UserAccount
        {
            Username = username, Name = name, Role = role, SbuId = sbuId, EmployeeId = employeeId, PasswordHash = passwords.Hash(password),
            Active = true, CreatedAt = now, UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(Aborted);
        audit.Record(Caller, "user", user.Id, null, "user_created", "role", null, role, $"{username} ({name}){(sbuId is null ? "" : $", SBU {sbuId}")}");
        await db.SaveChangesAsync(Aborted);
        return Status201(await GetAsync(user.Id));
    }

    /// <summary>Role, SBU, employee link, name, active, or a new password. A reason is recorded when given.</summary>
    [HttpPatch("users/{id}")]
    [RequireRole(Roles.UserManagers)]
    [FailureMessage("Failed to update the user")]
    public async Task<UserAccountDto> Update(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var userId = RequestValues.Id(id);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, Aborted) ?? throw new ApiException(StatusCodes.Status404NotFound, "User not found");
        var input = JsonBody.From(body);
        var name = input.OptionalString("name");
        var role = input.OptionalEnum("role", Roles.All);
        var sbuId = input.NullableInt("sbuId");
        var employeeId = input.NullableInt("employeeId");
        var active = input.Has("active") ? input.RequiredBool("active") : (bool?)null;
        var password = input.Has("password") ? input.RequiredString("password", minLength: 10) : null;
        var reason = input.OptionalString("reason");
        input.ThrowIfInvalid();

        // The caller must be allowed to manage the account as it is, and as it will be.
        var newRole = role ?? user.Role;
        var newSbu = sbuId.IsSet ? sbuId.Value : user.SbuId;
        var newEmployee = employeeId.IsSet ? employeeId.Value : user.EmployeeId;
        if (user.Role == Roles.Auditor || newRole == Roles.Auditor) throw AccessPolicy.Deny(Caller, "manage auditor accounts (Group Finance does, AU-12)");
        if (!Roles.All.Contains(user.Role) ? Caller.Role != Roles.GroupIt : !AccessPolicy.CanManageUser(Caller, user.Role, user.SbuId))
            throw AccessPolicy.Deny(Caller, "manage this account");
        if (!AccessPolicy.CanManageUser(Caller, newRole, newSbu)) throw AccessPolicy.Deny(Caller, $"give the {Roles.DisplayName(newRole)} role{(newSbu is null ? "" : " for this SBU")}");
        if (user.Id == Caller.Id && (newRole != user.Role || newSbu != user.SbuId || active == false))
            throw AccessPolicy.Deny(Caller, "change their own role, SBU or access");
        await ValidateAssignmentAsync(newRole, newSbu, newEmployee, user.Id);

        void Set<T>(string field, T before, T after, Action<T> apply)
        {
            if (audit.RecordChange(Caller, "user", user.Id, null, field, before, after, reason)) apply(after);
        }
        if (name is not null) Set("name", user.Name, name.Trim(), v => user.Name = v);
        Set("role", user.Role, newRole, v => user.Role = v);
        Set("sbuId", user.SbuId, newSbu, v => user.SbuId = v);
        Set("employeeId", user.EmployeeId, newEmployee, v => user.EmployeeId = v);
        if (active is not null) Set("active", user.Active, active.Value, v => user.Active = v);
        if (password is not null)
        {
            user.PasswordHash = passwords.Hash(password);
            audit.Record(Caller, "user", user.Id, null, "password_reset", "password", null, null, reason);
        }
        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(Aborted);
        return await GetAsync(user.Id);
    }

    /// <summary>SBU roles need their SBU; Group roles have none; the HOD and Employee need an employee of that SBU.</summary>
    private async Task ValidateAssignmentAsync(string role, int? sbuId, int? employeeId, int? exceptUserId)
    {
        if (Roles.IsSbuScoped(role) && sbuId is null) throw new ApiException(StatusCodes.Status400BadRequest, $"sbuId: required for the {Roles.DisplayName(role)} role");
        if (!Roles.IsSbuScoped(role) && sbuId is not null) throw new ApiException(StatusCodes.Status400BadRequest, $"sbuId: the {Roles.DisplayName(role)} role works across SBUs and has none");
        if (sbuId is not null && !await db.Sbus.AnyAsync(s => s.Id == sbuId, Aborted)) throw new ApiException(StatusCodes.Status404NotFound, "SBU not found");
        if (Roles.NeedEmployee.Contains(role) && employeeId is null)
            throw new ApiException(StatusCodes.Status400BadRequest, $"employeeId: the {Roles.DisplayName(role)} role must be linked to the employee's record");
        if (employeeId is { } eid)
        {
            var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eid, Aborted)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Employee not found");
            if (sbuId is not null && employee.SbuId != sbuId) throw new ApiException(StatusCodes.Status400BadRequest, "employeeId: the employee works for another SBU");
            if (await db.Users.AnyAsync(u => u.EmployeeId == eid && u.Id != exceptUserId, Aborted))
                throw new ApiException(StatusCodes.Status409Conflict, "That employee already has an account");
        }
    }

    private async Task<UserAccountDto> GetAsync(int id) => await Project(db.Users.AsNoTracking().Where(u => u.Id == id)).FirstAsync(Aborted);

    private IQueryable<UserAccountDto> Project(IQueryable<UserAccount> users) => users.Select(u => new UserAccountDto
    {
        Id = u.Id, Username = u.Username, Name = u.Name, Role = u.Role, RoleName = Roles.DisplayName(u.Role),
        SbuId = u.SbuId, SbuCode = db.Sbus.Where(s => s.Id == u.SbuId).Select(s => s.Code).FirstOrDefault(),
        EmployeeId = u.EmployeeId, EmployeeName = db.Employees.Where(e => e.Id == u.EmployeeId).Select(e => e.Name).FirstOrDefault(),
        AccessExpiresAt = u.AccessExpiresAt, Active = u.Active, LastLoginAt = u.LastLoginAt,
    });
}
