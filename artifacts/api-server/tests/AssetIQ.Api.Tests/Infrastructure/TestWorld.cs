using System.Text.Json;
using AssetIQ.Api.Data;

namespace AssetIQ.Api.Tests.Infrastructure;

/// <summary>
/// A small TSL world: two SBUs, an office and a gate per SBU, readers at each, and two TSAS assets
/// sitting in the TSAS IT office. Ids are 1, 2, … in the order below (the tables are reset first).
/// </summary>
public sealed class TestWorld
{
    public required Sbu Tsas { get; init; }
    public required Sbu Bak { get; init; }
    public required Location Office { get; init; }
    public required Location Gate { get; init; }
    public required Location BakGate { get; init; }
    public required Location BakWarehouse { get; init; }
    public required RfidReader OfficeReader { get; init; }
    public required RfidReader GateReader { get; init; }
    public required RfidReader BakGateReader { get; init; }
    public required RfidReader InactiveReader { get; init; }
    public required Asset Laptop { get; init; }
    public required Asset Projector { get; init; }

    public static async Task<TestWorld> SeedAsync(ApiFixture api)
    {
        await using var db = api.NewDbContext();
        var now = api.Now;

        var tsas = new Sbu { Code = "TSAS", Name = "Tobacco Sales Administration Services", Color = "#2563EB", CreatedAt = now, UpdatedAt = now };
        var bak = new Sbu { Code = "BAK", Name = "Bak Logistics", Color = "#DC2626", CreatedAt = now, UpdatedAt = now };
        db.Sbus.AddRange(tsas, bak);
        await db.SaveChangesAsync();

        Location NewLocation(string name, string type, string building, Sbu sbu) =>
            new() { Name = name, Type = type, Building = building, SbuId = sbu.Id, CreatedAt = now, UpdatedAt = now };
        var office = NewLocation("TSAS IT Office", "office", "TSAS House", tsas);
        var gate = NewLocation("TSAS Main Gate", "gate", "TSAS House", tsas);
        var bakGate = NewLocation("BAK Depot Gate", "gate", "BAK Depot", bak);
        var bakWarehouse = NewLocation("BAK Warehouse", "warehouse", "BAK Depot", bak);
        db.Locations.AddRange(office, gate, bakGate, bakWarehouse);
        await db.SaveChangesAsync();

        RfidReader NewReader(string name, Location location, string status = "active") =>
            new() { Name = name, LocationId = location.Id, Status = status, CreatedAt = now };
        var officeReader = NewReader("TSAS Office Reader", office);
        var gateReader = NewReader("TSAS Gate Reader", gate);
        var bakGateReader = NewReader("BAK Gate Reader", bakGate);
        var inactiveReader = NewReader("TSAS Spare Reader", office, "inactive");
        db.RfidReaders.AddRange(officeReader, gateReader, bakGateReader, inactiveReader);
        await db.SaveChangesAsync();

        var laptop = new Asset
        {
            Name = "Dell Latitude 5440", Category = "Laptop", SerialNumber = "SN-DL-001", RfidTag = "TAG-DL-001",
            SbuId = tsas.Id, CurrentLocationId = office.Id, AssignedTo = "John Doe", CreatedAt = now, UpdatedAt = now,
        };
        var projector = new Asset
        {
            Name = "Epson EB-X51 Projector", Category = "Projector", SerialNumber = "SN-EP-001", RfidTag = "TAG-EP-001",
            SbuId = tsas.Id, CurrentLocationId = office.Id, CreatedAt = now, UpdatedAt = now,
        };
        db.Assets.AddRange(laptop, projector);
        await db.SaveChangesAsync();

        return new TestWorld
        {
            Tsas = tsas, Bak = bak,
            Office = office, Gate = gate, BakGate = bakGate, BakWarehouse = bakWarehouse,
            OfficeReader = officeReader, GateReader = gateReader, BakGateReader = bakGateReader, InactiveReader = inactiveReader,
            Laptop = laptop, Projector = projector,
        };
    }
}

/// <summary>Terse access to JSON response bodies in assertions.</summary>
public static class JsonAssertions
{
    public static JsonElement Prop(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw new Xunit.Sdk.XunitException($"Missing property \"{name}\" in {element}");

    public static string? Str(this JsonElement element, string name) =>
        element.Prop(name).ValueKind == JsonValueKind.Null ? null : element.Prop(name).GetString();

    public static int Int(this JsonElement element, string name) => element.Prop(name).GetInt32();

    public static int? IntOrNull(this JsonElement element, string name) =>
        element.Prop(name).ValueKind == JsonValueKind.Null ? null : element.Prop(name).GetInt32();

    public static bool Bool(this JsonElement element, string name) => element.Prop(name).GetBoolean();

    public static bool IsNull(this JsonElement element, string name) => element.Prop(name).ValueKind == JsonValueKind.Null;

    public static bool Has(this JsonElement element, string name) => element.TryGetProperty(name, out _);

    public static List<JsonElement> Items(this JsonElement element) => element.EnumerateArray().ToList();

    public static string Error(this ApiResponse response) => response.Body.Str("error") ?? "";

    /// <summary>The names of the object's properties, sorted.</summary>
    public static List<string> Keys(this JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
}
