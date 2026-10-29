using AssetIQ.Api.Services.Templates;

namespace AssetIQ.Api.Tests.Unit;

/// <summary>AU-01 / AU-02 / GI-02 / AM-01: template validation and the generated description.</summary>
public sealed class TemplateRulesTests
{
    private static readonly Template Laptop = new(1, "COMP", "Computers & Laptops", true, ["Laptop", "Desktop"],
        "{SubCategory} – {Make} {Model}, {Processor}, {Ram}, {Storage}", "serial_number",
        [
            new(1, "make", "Brand", "text", null, true, null),
            new(2, "model", "Model", "text", null, true, null),
            new(3, "serial_number", "Serial number", "text", null, true, null),
            new(4, "processor", "Processor", "text", null, true, null),
            new(5, "ram", "RAM", "integer", "GB", true, null),
            new(6, "storage", "Storage", "text", null, true, null),
            new(7, "mac_address", "MAC address", "text", null, false, null),
        ]);

    private static readonly Template Vehicle = new(5, "MV", "Motor Vehicles", false, ["Light vehicle", "Truck"],
        "{Make} {Model} {Year} – {RegistrationNumber}", "vin",
        [
            new(20, "registration_number", "Registration number", "text", null, true, null),
            new(21, "vin", "Chassis number (VIN)", "text", null, true, null),
            new(22, "make", "Make", "text", null, true, null),
            new(23, "model", "Model", "text", null, true, null),
            new(24, "year", "Year", "integer", null, true, null),
            new(25, "fuel_type", "Fuel type", "list", null, true, ["Diesel", "Petrol"]),
        ]);

    private static Dictionary<string, string?> LaptopValues() => new()
    {
        ["make"] = "Dell", ["model"] = "Latitude 5440", ["serial_number"] = "5CG4182KXL", ["processor"] = "i5", ["ram"] = "16", ["storage"] = "512 GB SSD",
    };

    [Fact]
    public void AU_01_builds_the_laptop_description_from_the_template_pattern()
    {
        var result = TemplateRules.Validate(Laptop, "Laptop", LaptopValues(), noSerial: false);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("Laptop – Dell Latitude 5440, i5, 16 GB, 512 GB SSD", TemplateRules.BuildDescription(Laptop, "Laptop", result.Values));
    }

    [Fact]
    public void AU_01_builds_the_vehicle_description_from_its_own_pattern()
    {
        var values = new Dictionary<string, string?>
        {
            ["registration_number"] = "AGR 4471", ["vin"] = "AHTFR22G406012345", ["make"] = "Toyota", ["model"] = "Hilux", ["year"] = "2021", ["fuel_type"] = "diesel",
        };
        var result = TemplateRules.Validate(Vehicle, "Light vehicle", values, noSerial: false);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("Toyota Hilux 2021 – AGR 4471", TemplateRules.BuildDescription(Vehicle, "Light vehicle", result.Values));
        Assert.Equal("Diesel", result.Values["fuel_type"].Text); // list values take the template's spelling
    }

    [Fact]
    public void AU_01_a_required_template_field_missing_is_rejected()
    {
        var values = LaptopValues();
        values.Remove("processor");
        values["ram"] = " ";

        var result = TemplateRules.Validate(Laptop, "Laptop", values, noSerial: false);

        Assert.Contains("attributes.processor: Processor is required for Computers & Laptops", result.Errors);
        Assert.Contains("attributes.ram: RAM is required for Computers & Laptops", result.Errors);
    }

    [Fact]
    public void AU_01_a_field_that_does_not_belong_to_the_category_is_rejected()
    {
        var values = LaptopValues();
        values["mileage"] = "120000";

        var result = TemplateRules.Validate(Laptop, "Laptop", values, noSerial: false);

        Assert.Equal(["attributes.mileage: is not a field of Computers & Laptops"], result.Errors);
    }

    [Fact]
    public void AU_01_values_must_match_the_field_type_and_the_sub_category_list()
    {
        var values = LaptopValues();
        values["ram"] = "sixteen";

        var badType = TemplateRules.Validate(Laptop, "Laptop", values, noSerial: false);
        var badSub = TemplateRules.Validate(Laptop, "Tablet", LaptopValues(), noSerial: false);
        var noSub = TemplateRules.Validate(Laptop, null, LaptopValues(), noSerial: false);
        var badList = TemplateRules.Validate(Vehicle, "Truck", new Dictionary<string, string?> { ["fuel_type"] = "Coal" }, noSerial: false);

        Assert.Contains("attributes.ram: RAM must be a whole number", badType.Errors);
        Assert.Contains(badSub.Errors, e => e.StartsWith("subCategory: 'Tablet' is not a sub-category"));
        Assert.Contains(noSub.Errors, e => e.StartsWith("subCategory: Required"));
        Assert.Contains("attributes.fuel_type: Fuel type must be one of Diesel, Petrol", badList.Errors);
    }

    [Fact]
    public void AU_02_the_identifier_may_be_left_out_only_when_the_item_is_recorded_as_no_serial()
    {
        var values = LaptopValues();
        values.Remove("serial_number");

        Assert.Contains("attributes.serial_number: Serial number is required for Computers & Laptops", TemplateRules.Validate(Laptop, "Laptop", values, noSerial: false).Errors);
        Assert.True(TemplateRules.Validate(Laptop, "Laptop", values, noSerial: true).IsValid);
    }

    [Fact]
    public void AU_01_optional_tokens_without_a_value_leave_no_stray_separators()
    {
        var pattern = Laptop with { DescriptionPattern = "{SubCategory} – {Make} {Model}, {MacAddress}, {Storage}" };
        var result = TemplateRules.Validate(pattern, "Desktop", LaptopValues(), noSerial: false);

        Assert.Equal("Desktop – Dell Latitude 5440, 512 GB SSD", TemplateRules.BuildDescription(pattern, "Desktop", result.Values));
    }

    [Theory]
    [InlineData("5cg4182kxl ", "5CG4182KXL")]
    [InlineData(" AHTfr22g406012345", "AHTFR22G406012345")]
    public void AU_02_identifiers_are_compared_trimmed_and_upper_cased(string typed, string stored) =>
        Assert.Equal(stored, TemplateRules.NormaliseIdentifier(typed));

    [Theory]
    [InlineData("a4-5d-36-12-ab-cd", "A4:5D:36:12:AB:CD")]
    [InlineData("A45D.3612.ABCD", "A4:5D:36:12:AB:CD")]
    [InlineData("A4:5D:36:12:AB", null)]
    [InlineData("not a mac", null)]
    public void GI_02_mac_addresses_are_normalised_and_checked(string typed, string? stored) =>
        Assert.Equal(stored, TemplateRules.NormaliseMac(typed));

    [Theory]
    [InlineData("ram", "Ram")]
    [InlineData("registration_number", "RegistrationNumber")]
    [InlineData("capacity_kva", "CapacityKva")]
    public void Pattern_tokens_are_the_field_keys_in_pascal_case(string key, string token) =>
        Assert.Equal(token, TemplateRules.PascalCase(key));
}
