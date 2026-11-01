using TestCommander.Models;
using TestCommander.Services;
using Xunit;

namespace TestCommander.Tests;

public sealed class VillageComparisonBuilderTests
{
    [Fact]
    public void SortsVillagesAndShowsObservedStockChangesAndStorageUse()
    {
        var alpha = Create("Alpha", 150, 200, 300, 400, 1000, 800,
            [new ResourceChange("Wood", 50), new ResourceChange("Clay", -25)]);
        var beta = Create("Beta", 10, 20, 30, 40, 100, 80);

        var rows = VillageComparisonBuilder.Create([beta, alpha]);

        Assert.Equal(new[] { "Alpha", "Beta" }, rows.Select(row => row.VillageName));
        Assert.Contains("150", rows[0].Wood);
        Assert.Contains("Δ +50", rows[0].Wood);
        Assert.Contains("Δ -25", rows[0].Clay);
        Assert.Equal("50.0%", rows[0].GranaryUtilization);
        Assert.Contains("W 15.0%", rows[0].WarehouseUtilization);
    }

    [Fact]
    public void KeepsMissingStocksAndCapacitiesExplicit()
    {
        var overview = Create("Partial", null, 20, 30, null, null, null);

        var row = Assert.Single(VillageComparisonBuilder.Create([overview]));

        Assert.Equal("Not detected", row.Wood);
        Assert.Equal("Not detected", row.Crop);
        Assert.Equal("N/A", row.GranaryUtilization);
        Assert.Contains("W N/A", row.WarehouseUtilization);
        Assert.Contains("C N/A", row.WarehouseUtilization);
    }

    [Fact]
    public void FlagsOverCapacityAndKeepsTheObservedStock()
    {
        var overview = Create("Full", 1200, 0, 0, 900, 1000, 800);

        var row = Assert.Single(VillageComparisonBuilder.Create([overview]));

        Assert.Contains("1,200", row.Wood);
        Assert.Contains("120.0% (over)", row.WarehouseUtilization);
        Assert.Equal("100.0% (over)", row.GranaryUtilization);
    }

    [Fact]
    public void ShowsUnknownDeltaUntilASecondComparableSampleExists()
    {
        var overview = Create("New", 10, 20, 30, 40, 100, 100);

        var row = Assert.Single(VillageComparisonBuilder.Create([overview]));

        Assert.Contains("Δ —", row.Wood);
    }

    private static VillageResourceOverview Create(
        string name, int? wood, int? clay, int? iron, int? crop,
        int? warehouse, int? granary, IReadOnlyList<ResourceChange>? changes = null)
    {
        var captured = DateTimeOffset.UtcNow;
        var resources = new[]
        {
            new ResourceGauge("Wood", wood, warehouse),
            new ResourceGauge("Clay", clay, warehouse),
            new ResourceGauge("Iron", iron, warehouse),
            new ResourceGauge("Crop", crop, granary)
        };
        return new VillageResourceOverview(name, captured, resources, changes ?? [], []);
    }
}
