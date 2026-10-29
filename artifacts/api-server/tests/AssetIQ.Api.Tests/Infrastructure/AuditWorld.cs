using AssetIQ.Api.Domain;
using AssetIQ.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetIQ.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="TestWorld"/> plus one account per scoped role in TSAS and BAK (Phase 1), and helpers that
/// register and capitalise assets through the API exactly as the dashboard does.
/// </summary>
public sealed class AuditWorld
{
    public required TestWorld W { get; init; }
    public required string TsasIt { get; init; }
    public required string TsasFinance { get; init; }
    public required string TsasAdmin { get; init; }
    public required string TsasStores { get; init; }
    public required string BakIt { get; init; }
    public required string BakFinance { get; init; }
    public required string BakAdmin { get; init; }
    public required string GroupFinance { get; init; }
    public required string GroupIt { get; init; }

    public static async Task<AuditWorld> SeedAsync(ApiFixture api)
    {
        var w = await TestWorld.SeedAsync(api);
        return new AuditWorld
        {
            W = w,
            TsasIt = await Accounts.TokenAsync(api, Roles.SbuIt, w.Tsas),
            TsasFinance = await Accounts.TokenAsync(api, Roles.SbuFinance, w.Tsas, name: "Chipo Finance"),
            TsasAdmin = await Accounts.TokenAsync(api, Roles.SbuAdmin, w.Tsas),
            TsasStores = await Accounts.TokenAsync(api, Roles.Stores, w.Tsas),
            BakIt = await Accounts.TokenAsync(api, Roles.SbuIt, w.Bak),
            BakFinance = await Accounts.TokenAsync(api, Roles.SbuFinance, w.Bak),
            BakAdmin = await Accounts.TokenAsync(api, Roles.SbuAdmin, w.Bak),
            GroupFinance = await Accounts.TokenAsync(api, Roles.GroupFinance),
            GroupIt = await Accounts.TokenAsync(api, Roles.GroupIt),
        };
    }

    public static object LaptopBody(string serial, int sbuId, int locationId, string? mac = null, string ram = "16") => new
    {
        categoryId = Categories.Computers, subCategory = "Laptop", sbuId, currentLocationId = locationId, assignedTo = "SBU IT store", condition = "new",
        poNo = "PO-TSAS-2026-0100", supplier = "Axis Computers", purchaseDate = "2026-01-10",
        attributes = new Dictionary<string, object?>
        {
            ["make"] = "Dell", ["model"] = "Latitude 5440", ["serial_number"] = serial, ["processor"] = "i5-1345U", ["ram"] = int.Parse(ram),
            ["storage"] = "512 GB SSD", ["operating_system"] = "Windows 11 Pro", ["mac_address"] = mac,
        },
    };

    /// <summary>Registers a TSAS laptop as SBU IT; Business Central (mock) issues its number straight away.</summary>
    public async Task<int> RegisterLaptopAsync(ApiFixture api, string serial, string? mac = null)
    {
        var res = await api.CallAsync("POST", "/assets", LaptopBody(serial, W.Tsas.Id, W.Office.Id, mac), TsasIt);
        Assert.True(res.Status == 201, $"register: {res.Status} {res.Error()}");
        return res.Body.Int("id");
    }

    /// <summary>SBU Finance records the cost and capitalises the asset.</summary>
    public async Task CapitaliseAsync(ApiFixture api, int assetId, decimal cost, string inUseDate, string finance, string currency = "USD", decimal rate = 1m)
    {
        var fin = await api.CallAsync("PATCH", $"/assets/{assetId}/financial",
            new { originalCost = cost, currency, exchangeRate = rate, inUseDate, invoiceNo = $"INV-{assetId}", poNo = $"PO-{assetId}", reason = "Capitalisation from invoice" }, finance);
        Assert.True(fin.Status == 200, $"financial: {fin.Status} {fin.Error()}");
        var cap = await api.CallAsync("POST", $"/assets/{assetId}/capitalise", new { }, finance);
        Assert.True(cap.Status == 200, $"capitalise: {cap.Status} {cap.Error()}");
    }

    /// <summary>Runs the Business Central number queue as the background worker would.</summary>
    public static async Task<int> ProcessBcQueueAsync(ApiFixture api)
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BcNumberService>().ProcessDueAsync(CancellationToken.None);
    }

    public static async Task<List<Data.AuditEntry>> AuditAsync(ApiFixture api, int assetId)
    {
        await using var db = api.NewDbContext();
        return await db.AuditLog.Where(e => e.AssetId == assetId).OrderBy(e => e.Id).ToListAsync();
    }
}
