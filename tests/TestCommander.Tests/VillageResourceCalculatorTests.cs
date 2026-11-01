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

    private static VillageSnapshot CreateSnapshot(int? wood, int? clay, int? iron, int? crop, int? warehouse, int? granary) =>
        new("Test village", wood, clay, iron, crop, warehouse, granary, DateTimeOffset.UtcNow);
}