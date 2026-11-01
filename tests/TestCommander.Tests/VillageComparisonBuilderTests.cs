using TestCommander.Models;
using TestCommander.Services;
using Xunit;

namespace TestCommander.Tests;

public sealed class VillageComparisonBuilderTests
{
    [Fact]
    public void ComparisonSortsVillagesAndShowsStockCapacityUtilizationAndObservedDelta()
    {
        var overview = VillageResourceCalculator.Create(
            new VillageSnapshot("Zulu", 750, 500, 250, 1000, 1000, 2000, DateTimeOffset.UtcNow),
            new VillageSnapshot("Zulu", 650, 500, 250, 900, 1000, 2000, DateTimeOffset.UtcNow.AddMinutes(-5)));
        var other = VillageResourceCalculator.Create(
            new VillageSnapshot("Alpha", 100, 200, 300, 400, 1000, 1000, DateTimeOffset.UtcNow));

        var rows = VillageComparisonBuilder.Create([overview, other]);

        Assert.Equal(new[] { "Alpha", "Zulu" }, rows.Select(row => row.VillageName));
        Assert.Contains("750", rows[1].Wood);
        Assert.Contains("1000", rows[1].Wood);
        Assert.Contains("75%", rows[1].Wood);
        Assert.Contains("Δ +100", rows[1].Wood);
    }

    [Fact]
    public void ComparisonKeepsMissingStockAndCapacityAsUnknown()
    {
        var overview = VillageResourceCalculator.Create(
            new VillageSnapshot("Partial", null, 250, null, 500, null, 0, DateTimeOffset.UtcNow));

        var row = Assert.Single(VillageComparisonBuilder.Create([overview]));

        Assert.Equal("— / cap — · — · Δ —", row.Wood);
        Assert.Equal("250 / cap — · — · Δ —", row.Clay);
        Assert.Equal("— / cap — · — · Δ —", row.Iron);
        Assert.Equal("500 / cap — · — · Δ —", row.Crop);
    }

    [Fact]
    public void ComparisonDoesNotInventChangeWhenPreviousSampleIsMissing()
    {
        var overview = VillageResourceCalculator.Create(
            new VillageSnapshot("New village", 10, 20, 30, 40, 100, 100, DateTimeOffset.UtcNow));

        var row = Assert.Single(VillageComparisonBuilder.Create([overview]));

        Assert.Contains("Δ —", row.Wood);
    }
}
