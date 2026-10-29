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
/// Departments and employees (Phase 2): SBU HR maintains its own SBU's; Group roles read every SBU's; the HOD
/// reads their department; the SBU roles that assign or account for items read their SBU's. The toolbox
/// workflows (HR-01 … HR-06) build on these in Phase 4.
/// </summary>
public sealed class PeopleController(AssetIqDbContext db, AppClock clock, AuditTrail audit) : ApiControllerBase
{
    [HttpGet("departments")]
    [RequireRole(Roles.PeopleReaders)]
    [FailureMessage("Failed to list departments")]
    public Task<List<DepartmentDto>> Departments([FromQuery] string? sbuId)
    {
        var sbuFilter = RequestValues.OptionalInt(sbuId);
        var query = db.Departments.AsNoTracking();
        if (Roles.IsSbuScoped(Caller.Role)) query = query.Where(d => d.SbuId == Caller.SbuId);
        if (Caller.Role == Roles.Hod) query = query.Where(d => d.Id == Caller.DepartmentId);
        if (sbuFilter is not null) query = query.Where(d => d.SbuId == sbuFilter);
        return query.OrderBy(d => d.SbuId).ThenBy(d => d.Name)
            .Select(d => new DepartmentDto(d.Id, d.SbuId, db.Sbus.Where(s => s.Id == d.SbuId).Select(s => s.Code).First(), d.Name,
                db.Employees.Count(e => e.DepartmentId == d.Id && e.Active)))
            .ToListAsync(Aborted);
    }

    [HttpPost("departments")]
    [RequireRole(Roles.SbuHr)]
    [FailureMessage("Failed to create the department")]
    public async Task<IActionResult> CreateDepartment([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var name = input.RequiredString("name", minLength: 2).Trim();
        var sbuId = input.OptionalInt("sbuId") ?? Caller.SbuId ?? 0;
        input.ThrowIfInvalid();
        if (!AccessPolicy.CanManagePeople(Caller, sbuId)) throw AccessPolicy.Deny(Caller, "add departments to another SBU");
        if (await db.Departments.AnyAsync(d => d.SbuId == sbuId && d.Name == name, Aborted))
            throw new ApiException(StatusCodes.Status409Conflict, "The SBU already has a department with that name");
        var now = clock.UtcNow;
        var department = new Department { SbuId = sbuId, Name = name, CreatedAt = now, UpdatedAt = now };
        db.Departments.Add(department);
        await db.SaveChangesAsync(Aborted);
        audit.Record(Caller, "department", department.Id, null, "created", "name", null, name);
        await db.SaveChangesAsync(Aborted);
        return Status201((await Departments(null)).First(d => d.Id == department.Id));
    }

    [HttpGet("employees")]
    [RequireRole(Roles.PeopleReaders)]
    [FailureMessage("Failed to list employees")]
    public Task<List<EmployeeDto>> Employees([FromQuery] string? sbuId, [FromQuery] string? departmentId, [FromQuery] string? q, [FromQuery] string? includeLeavers)
    {
        var sbuFilter = RequestValues.OptionalInt(sbuId);
        var departmentFilter = RequestValues.OptionalInt(departmentId);
        var search = RequestValues.OptionalText(q);
        var query = db.Employees.AsNoTracking();
        if (Roles.IsSbuScoped(Caller.Role)) query = query.Where(e => e.SbuId == Caller.SbuId);
        if (Caller.Role == Roles.Hod) query = query.Where(e => e.DepartmentId == Caller.DepartmentId);
        if (sbuFilter is not null) query = query.Where(e => e.SbuId == sbuFilter);
        if (departmentFilter is not null) query = query.Where(e => e.DepartmentId == departmentFilter);
        if (!string.Equals(includeLeavers, "true", StringComparison.OrdinalIgnoreCase)) query = query.Where(e => e.Active);
        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(e => EF.Functions.ILike(e.Name, pattern) || EF.Functions.ILike(e.EmployeeNo, pattern));
        }
        return Project(query.OrderBy(e => e.Name)).ToListAsync(Aborted);
    }

    [HttpGet("employees/{id}")]
    [RequireRole(Roles.PeopleReaders)]
    [FailureMessage("Failed to get the employee")]
    public async Task<EmployeeDto> Employee(string id)
    {
        var employeeId = RequestValues.Id(id);
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Employee not found");
        if (!AccessPolicy.CanViewPerson(Caller, employee.SbuId, employee.DepartmentId)) throw AccessPolicy.Deny(Caller, "see this employee");
        return await Project(db.Employees.AsNoTracking().Where(e => e.Id == employeeId)).FirstAsync(Aborted);
    }

    [HttpPost("employees")]
    [RequireRole(Roles.SbuHr)]
    [FailureMessage("Failed to create the employee")]
    public async Task<IActionResult> CreateEmployee([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var employeeNo = input.RequiredString("employeeNo", minLength: 2).Trim().ToUpperInvariant();
        var name = input.RequiredString("name", minLength: 2).Trim();
        var sbuId = input.OptionalInt("sbuId") ?? Caller.SbuId ?? 0;
        var departmentId = input.OptionalInt("departmentId");
        var lineManagerId = input.OptionalInt("lineManagerId");
        var jobRole = input.OptionalString("jobRole");
        var email = input.OptionalString("email");
        var phone = input.OptionalString("phone");
        input.ThrowIfInvalid();
        if (!AccessPolicy.CanManagePeople(Caller, sbuId)) throw AccessPolicy.Deny(Caller, "add employees to another SBU");
        await CheckLinksAsync(sbuId, departmentId, lineManagerId, null);
        if (await db.Employees.AnyAsync(e => e.EmployeeNo == employeeNo, Aborted)) throw new ApiException(StatusCodes.Status409Conflict, "That employee number is taken");

        var now = clock.UtcNow;
        var employee = new Employee
        {
            SbuId = sbuId, DepartmentId = departmentId, EmployeeNo = employeeNo, Name = name, JobRole = Blank(jobRole), LineManagerId = lineManagerId,
            Email = Blank(email), Phone = Blank(phone), Active = true, CreatedAt = now, UpdatedAt = now,
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync(Aborted);
        audit.Record(Caller, "employee", employee.Id, null, "created", "name", null, $"{employeeNo} {name}");
        await db.SaveChangesAsync(Aborted);
        return Status201(await Project(db.Employees.AsNoTracking().Where(e => e.Id == employee.Id)).FirstAsync(Aborted));
    }

    [HttpPatch("employees/{id}")]
    [RequireRole(Roles.SbuHr)]
    [FailureMessage("Failed to update the employee")]
    public async Task<EmployeeDto> UpdateEmployee(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var employeeId = RequestValues.Id(id);
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Employee not found");
        if (!AccessPolicy.CanManagePeople(Caller, employee.SbuId)) throw AccessPolicy.Deny(Caller, "change another SBU's employees");
        var input = JsonBody.From(body);
        var name = input.OptionalString("name");
        var departmentId = input.NullableInt("departmentId");
        var lineManagerId = input.NullableInt("lineManagerId");
        var jobRole = input.NullableString("jobRole");
        var email = input.NullableString("email");
        var phone = input.NullableString("phone");
        var active = input.Has("active") ? input.RequiredBool("active") : (bool?)null;
        var reason = input.OptionalString("reason");
        input.ThrowIfInvalid();
        await CheckLinksAsync(employee.SbuId, departmentId.IsSet ? departmentId.Value : null, lineManagerId.IsSet ? lineManagerId.Value : null, employee.Id);
        // ASSUMPTION: an employee who still holds items cannot be marked as left; exit clearance (HR-04, Phase 4) returns them first.
        if (active == false && employee.Active && await db.Assets.IgnoreQueryFilters().AnyAsync(a => a.CustodianEmployeeId == employee.Id, Aborted))
            throw new ApiException(StatusCodes.Status409Conflict, "The employee still holds company items; complete the exit clearance first (HR-04)");

        void Set<T>(string field, T before, T after, Action<T> apply)
        {
            if (audit.RecordChange(Caller, "employee", employee.Id, null, field, before, after, reason)) apply(after);
        }
        if (name is not null) Set("name", employee.Name, name.Trim(), v => employee.Name = v);
        if (departmentId.IsSet) Set("departmentId", employee.DepartmentId, departmentId.Value, v => employee.DepartmentId = v);
        if (lineManagerId.IsSet) Set("lineManagerId", employee.LineManagerId, lineManagerId.Value, v => employee.LineManagerId = v);
        if (jobRole.IsSet) Set("jobRole", employee.JobRole, Blank(jobRole.Value), v => employee.JobRole = v);
        if (email.IsSet) Set("email", employee.Email, Blank(email.Value), v => employee.Email = v);
        if (phone.IsSet) Set("phone", employee.Phone, Blank(phone.Value), v => employee.Phone = v);
        if (active is not null) Set("active", employee.Active, active.Value, v => employee.Active = v);
        employee.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(Aborted);
        return await Project(db.Employees.AsNoTracking().Where(e => e.Id == employee.Id)).FirstAsync(Aborted);
    }

    private async Task CheckLinksAsync(int sbuId, int? departmentId, int? lineManagerId, int? self)
    {
        if (!await db.Sbus.AnyAsync(s => s.Id == sbuId, Aborted)) throw new ApiException(StatusCodes.Status404NotFound, "SBU not found");
        if (departmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d && x.SbuId == sbuId, Aborted))
            throw new ApiException(StatusCodes.Status400BadRequest, "departmentId: not a department of the employee's SBU");
        if (lineManagerId is { } m && (m == self || !await db.Employees.AnyAsync(x => x.Id == m && x.SbuId == sbuId, Aborted)))
            throw new ApiException(StatusCodes.Status400BadRequest, "lineManagerId: not another employee of the same SBU");
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Items held counts what the caller may see of the employee's items (the query filter applies).
    private IQueryable<EmployeeDto> Project(IQueryable<Employee> employees) => employees.Select(e => new EmployeeDto
    {
        Id = e.Id, SbuId = e.SbuId, SbuCode = db.Sbus.Where(s => s.Id == e.SbuId).Select(s => s.Code).First(),
        DepartmentId = e.DepartmentId, DepartmentName = e.Department!.Name, EmployeeNo = e.EmployeeNo, Name = e.Name, JobRole = e.JobRole,
        LineManagerId = e.LineManagerId, LineManagerName = db.Employees.Where(m => m.Id == e.LineManagerId).Select(m => m.Name).FirstOrDefault(),
        Email = e.Email, Phone = e.Phone, Active = e.Active,
        ItemsHeld = db.Assets.Count(a => a.CustodianEmployeeId == e.Id),
    });
}
