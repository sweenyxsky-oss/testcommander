using TestCommander.Models;
using TestCommander.Services;
using Xunit;

namespace TestCommander.Tests;

public sealed class VillageResourceCalculatorTests
{
    [Fact]
    public void CalculatesWarehouseUtilizationForWoodClayAndIron()
    {
        var snapshot = CreateSnapshot(wood: 1200, clay: 600, iron: 2400, crop: 300, warehouse: 2400, granary: 1200);

        var result = VillageResourceCalculator.Create(snapshot);

        Assert.Equal(50d, result.Resources[0].UtilizationPercent);
        Assert.Equal(25d, result.Resources[1].UtilizationPercent);
        Assert.Equal(100d, result.Resources[2].UtilizationPercent);
        Assert.Equal(1200, result.Resources[0].RemainingCapacity);
    }

    [Fact]
    public void UsesGranaryCapacityForCropRatherThanWarehouse()
    {
        var snapshot = CreateSnapshot(wood: 100, clay: 100, iron: 100, crop: 600, warehouse: 1000, granary: 1200);

        var result = VillageResourceCalculator.Create(snapshot);

        var crop = Assert.Single(result.Resources, resource => resource.Name == "Crop");
        Assert.Equal(50d, crop.UtilizationPercent);
        Assert.Equal(600, crop.RemainingCapacity);
    }

    [Fact]
    public void ClampsVisualPercentageAndFlagsOverCapacity()
    {
        var snapshot = CreateSnapshot(wood: 1500, clay: 0, iron: 0, crop: 0, warehouse: 1000, granary: 1000);

        var result = VillageResourceCalculator.Create(snapshot);
        var wood = result.Resources[0];

        Assert.Equal(100d, wood.UtilizationPercent);
        Assert.True(wood.IsOverCapacity);
        Assert.Equal("Over capacity", wood.UtilizationLabel);
        Assert.Equal(0, wood.RemainingCapacity);
    }

    [Fact]
    public void DoesNotInventUtilizationWhenCapacityIsUnknown()
    {
        var snapshot = CreateSnapshot(wood: 100, clay: 100, iron: 100, crop: 100, warehouse: null, granary: null);

        var result = VillageResourceCalculator.Create(snapshot);

        Assert.All(result.Resources, gauge =>
        {
            Assert.False(gauge.HasCapacity);
            Assert.Equal(0d, gauge.UtilizationPercent);
            Assert.Null(gauge.RemainingCapacity);
            Assert.Equal("Capacity not detected", gauge.UtilizationLabel);
        });
    }

    [Fact]
    public void HandlesMissingStockWithoutShowingAFalseZero()
    {
        var snapshot = CreateSnapshot(wood: null, clay: 20, iron: 30, crop: 40, warehouse: 100, granary: 100);

        var result = VillageResourceCalculator.Create(snapshot);

        Assert.Equal("Not detected", result.Resources[0].StockLabel);
        Assert.False(result.Resources[0].HasCapacity);
        Assert.Equal("Stock not detected", result.Resources[0].UtilizationLabel);
    }

    [Fact]
    public void CalculatesObservedChangesWithoutCallingThemProduction()
    {
        var previous = CreateSnapshot(wood: 100, clay: 200, iron: 300, crop: 400, warehouse: 1000, granary: 800);
        var current = CreateSnapshot(wood: 150, clay: 175, iron: 300, crop: 450, warehouse: 1000, granary: 800);

        var result = VillageResourceCalculator.Create(current, previous);

        Assert.Equal(4, result.Changes.Count);
        Assert.Equal(50, result.Changes.Single(change => change.Name == "Wood").Delta);
        Assert.Equal(-25, result.Changes.Single(change => change.Name == "Clay").Delta);
        Assert.Equal(0, result.Changes.Single(change => change.Name == "Iron").Delta);
        Assert.Contains("Since previous distinct sample", result.HistorySummary);
        Assert.DoesNotContain("per hour", result.HistorySummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkipsChangesWhenEitherSnapshotValueIsUnknown()
    {
        var previous = CreateSnapshot(wood: null, clay: 200, iron: 300, crop: 400, warehouse: 1000, granary: 800);
        var current = CreateSnapshot(wood: 150, clay: null, iron: 350, crop: 450, warehouse: 1000, granary: 800);

        var result = VillageResourceCalculator.Create(current, previous);

        Assert.Equal(new[] { "Iron", "Crop" }, result.Changes.Select(change => change.Name));
    }

    [Fact]
    public void IncludesTrendSeriesFromTheSuppliedSnapshotHistory()
    {
        var history = new[]
        {
            new VillageSnapshot("Test village", 100, 200, 300, 400, 1000, 1000, DateTimeOffset.UtcNow.AddMinutes(-2)),
            new VillageSnapshot("Test village", 150, 250, 350, 450, 1000, 1000, DateTimeOffset.UtcNow.AddMinutes(-1)),
            new VillageSnapshot("Test village", 200, 300, 400, 500, 1000, 1000, DateTimeOffset.UtcNow)
        };

        var result = VillageResourceCalculator.Create(history[^1], history[^2], history);

        Assert.Equal(4, result.Trends.Count);
        Assert.Equal(3, result.Trends[0].SampleCount);
        Assert.Contains("Δ +100", result.Trends[0].ChangeLabel);
    }

    [Fact]
    public void DoesNotCompareSnapshotsFromDifferentVillages()
    {
        var previous = new VillageSnapshot("Village A", 100, 200, 300, 400, 1000, 800, DateTimeOffset.UtcNow);
        var current = new VillageSnapshot("Village B", 150, 250, 350, 450, 1000, 800, DateTimeOffset.UtcNow);

        var result = VillageResourceCalculator.Create(current, previous);

        Assert.Empty(result.Changes);
        Assert.Contains("Waiting for a second sample", result.HistorySummary);
    }

    private static VillageSnapshot CreateSnapshot(int? wood, int? clay, int? iron, int? crop, int? warehouse, int? granary) =>
        new("Test village", wood, clay, iron, crop, warehouse, granary, DateTimeOffset.UtcNow);
}