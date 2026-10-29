using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;

namespace AssetIQ.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="TestWorld"/> plus what the Phase 2 permission tests need: one account per role in TSAS ("SBU A"),
/// departments and employees in both SBUs, and BAK ("SBU B") records of every kind to try to read, edit and
/// export. TSAS: the laptop is held by <see cref="TsasStaff"/> of <see cref="TsasOps"/>, the projector sits
/// unassigned in the IT office (an item in store), and a desk (non-IT) is held by the same employee.
/// </summary>
public sealed class RoleWorld
{
    public required TestWorld W { get; init; }
    public required Department TsasOps { get; init; }
    public required Department BakOps { get; init; }
    public required Employee TsasHodEmployee { get; init; }
    public required Employee TsasStaff { get; init; }
    public required Employee BakStaff { get; init; }
    public required Asset TsasDesk { get; init; }
    public required Asset BakLaptop { get; init; }
    public required Asset BakDesk { get; init; }
    public required Asset BakStoreItem { get; init; }
    public required GatePass TsasPass { get; init; }
    public required GatePass BakPass { get; init; }
    /// <summary>A token per role. SBU roles are TSAS accounts; the HOD heads TSAS Operations; the Employee is <see cref="TsasStaff"/>.</summary>
    public required IReadOnlyDictionary<string, string> Tsas { get; init; }

    public string this[string role] => Tsas[role];

    public static async Task<RoleWorld> SeedAsync(ApiFixture api)
    {
        var w = await TestWorld.SeedAsync(api);
        await using var db = api.NewDbContext();
        var now = api.Now;
        var tsasOps = new Department { SbuId = w.Tsas.Id, Name = "Operations", CreatedAt = now, UpdatedAt = now };
        var bakOps = new Department { SbuId = w.Bak.Id, Name = "Operations", CreatedAt = now, UpdatedAt = now };
        db.Departments.AddRange(tsasOps, bakOps);
        await db.SaveChangesAsync();

        Employee NewEmployee(string no, string name, Sbu sbu, Department department) => new()
        {
            EmployeeNo = no, Name = name, SbuId = sbu.Id, DepartmentId = department.Id, JobRole = "Officer", Active = true, CreatedAt = now, UpdatedAt = now,
        };
        var hod = NewEmployee("TSAS-0001", "Rudo Moyo", w.Tsas, tsasOps);
        var staff = NewEmployee("TSAS-0002", "John Doe", w.Tsas, tsasOps);
        var bakStaff = NewEmployee("BAK-0001", "Tapiwa Ndlovu", w.Bak, bakOps);
        db.Employees.AddRange(hod, staff, bakStaff);
        await db.SaveChangesAsync();

        var laptop = await db.Assets.FindAsync(w.Laptop.Id);
        laptop!.CustodianEmployeeId = staff.Id;
        laptop.DepartmentId = tsasOps.Id;
        Asset NewAsset(string name, int categoryId, string sub, string serial, string number, Sbu sbu, Location location, Employee? holder, Department? department) => new()
        {
            Name = name, Category = sub, CategoryId = categoryId, SubCategory = sub, SerialNumber = serial, RfidTag = $"TAG-{serial}", AssetNumber = number,
            SbuId = sbu.Id, CurrentLocationId = location.Id, AssignedTo = holder?.Name, CustodianEmployeeId = holder?.Id, DepartmentId = department?.Id,
            CostUsd = 1200m, OriginalCost = 1200m, Currency = "USD", ExchangeRate = 1m, PurchaseValue = 1200m, CreatedAt = now, UpdatedAt = now,
        };
        var tsasDesk = NewAsset("Desk – Generic Executive", Categories.Furniture, "Desk", "SN-DESK-1", "TSAS-FF-2026-00001", w.Tsas, w.Office, staff, tsasOps);
        var bakLaptop = NewAsset("Laptop – HP EliteBook 840", Categories.Computers, "Laptop", "SN-BAK-1", "BAK-IT-2026-00001", w.Bak, w.BakWarehouse, bakStaff, bakOps);
        var bakDesk = NewAsset("Desk – Generic Office", Categories.Furniture, "Desk", "SN-BAK-2", "BAK-FF-2026-00001", w.Bak, w.BakWarehouse, bakStaff, bakOps);
        var bakStore = NewAsset("Laptop – Dell Latitude 3540", Categories.Computers, "Laptop", "SN-BAK-3", "BAK-IT-2026-00002", w.Bak, w.BakWarehouse, null, null);
        db.Assets.AddRange(tsasDesk, bakLaptop, bakDesk, bakStore);
        await db.SaveChangesAsync();

        GatePass NewPass(Asset asset, string by) => new()
        {
            AssetId = asset.Id, RequestedBy = by, Reason = "Field work", Status = GatePassStatuses.Pending, Source = GatePassSources.Manual,
            ReturnDate = "2026-10-20", CreatedAt = now, UpdatedAt = now,
        };
        var tsasPass = NewPass(w.Laptop, staff.Name);
        var bakPass = NewPass(bakLaptop, bakStaff.Name);
        db.GatePasses.AddRange(tsasPass, bakPass);
        await db.SaveChangesAsync();

        var tokens = new Dictionary<string, string>
        {
            [Roles.GroupFinance] = await Accounts.TokenAsync(api, Roles.GroupFinance),
            [Roles.GroupIt] = await Accounts.TokenAsync(api, Roles.GroupIt),
            [Roles.SbuIt] = await Accounts.TokenAsync(api, Roles.SbuIt, w.Tsas),
            [Roles.SbuFinance] = await Accounts.TokenAsync(api, Roles.SbuFinance, w.Tsas),
            [Roles.GateOfficer] = await Accounts.TokenAsync(api, Roles.GateOfficer, w.Tsas),
            [Roles.Hod] = await Accounts.TokenAsync(api, Roles.Hod, w.Tsas, employeeId: hod.Id),
            [Roles.SbuHr] = await Accounts.TokenAsync(api, Roles.SbuHr, w.Tsas),
            [Roles.Stores] = await Accounts.TokenAsync(api, Roles.Stores, w.Tsas),
            [Roles.SbuAdmin] = await Accounts.TokenAsync(api, Roles.SbuAdmin, w.Tsas),
            [Roles.Auditor] = await Accounts.TokenAsync(api, Roles.Auditor, accessExpiresAt: now.AddDays(30)),
            [Roles.Employee] = await Accounts.TokenAsync(api, Roles.Employee, w.Tsas, employeeId: staff.Id),
            [Roles.SystemAdmin] = await Accounts.TokenAsync(api, Roles.SystemAdmin),
        };
        return new RoleWorld
        {
            W = w, TsasOps = tsasOps, BakOps = bakOps, TsasHodEmployee = hod, TsasStaff = staff, BakStaff = bakStaff,
            TsasDesk = tsasDesk, BakLaptop = bakLaptop, BakDesk = bakDesk, BakStoreItem = bakStore, TsasPass = tsasPass, BakPass = bakPass,
            Tsas = tokens,
        };
    }
}
