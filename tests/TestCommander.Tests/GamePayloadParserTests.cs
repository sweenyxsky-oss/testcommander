using System.Text.Json;
using TestCommander.Services;
using Xunit;

namespace TestCommander.Tests;

public sealed class GamePayloadParserTests
{
    [Fact]
    public void ParsesNamedResourcesAndStorageCapacities()
    {
        using var json = JsonDocument.Parse("""
            {
              "villageName": "New Rome",
              "resources": { "wood": 1200, "clay": 900, "iron": 800, "crop": 1500 },
              "capacities": { "warehouseCapacity": 2400, "granaryCapacity": 1800 }
            }
            """);

        var result = GamePayloadParser.TryCreateSnapshot(json.RootElement);

        Assert.NotNull(result);
        Assert.Equal("New Rome", result.VillageName);
        Assert.Equal(1200, result.Wood);
        Assert.Equal(900, result.Clay);
        Assert.Equal(800, result.Iron);
        Assert.Equal(1500, result.Crop);
        Assert.Equal(2400, result.WarehouseCapacity);
        Assert.Equal(1800, result.GranaryCapacity);
    }

    [Fact]
    public void SupportsDirectResourceFieldsAndNumericStrings()
    {
        using var json = JsonDocument.Parse("""{"name":"Village 2","wood":"150","clay":0,"iron":25,"crop":100}""");

        var result = GamePayloadParser.TryCreateSnapshot(json.RootElement);

        Assert.NotNull(result);
        Assert.Equal("Village 2", result.VillageName);
        Assert.Equal(150, result.Wood);
        Assert.Equal(0, result.Clay);
        Assert.Equal(25, result.Iron);
        Assert.Equal(100, result.Crop);
    }

    [Fact]
    public void DoesNotCreateHistoryForResourcesWithoutVillageIdentity()
    {
        using var json = JsonDocument.Parse("""{"resources":{"wood":10,"clay":20,"iron":30,"crop":40}}""");

        Assert.Null(GamePayloadParser.TryCreateSnapshot(json.RootElement));
    }

    [Fact]
    public void DoesNotMistakeBuildCostsForCurrentVillageStock()
    {
        using var json = JsonDocument.Parse("""
            {
              "buildingQueue": [
                { "cost": { "wood": 500, "clay": 400, "iron": 300, "crop": 200 } }
              ]
            }
            """);

        Assert.Null(GamePayloadParser.TryCreateSnapshot(json.RootElement));
    }

    [Fact]
    public void RejectsNegativeAndOutOfRangeValuesInsteadOfDisplayingThem()
    {
        using var json = JsonDocument.Parse("""{"resources":{"wood":-5,"clay":999999999999,"iron":"-2","crop":75}}""");

        var result = GamePayloadParser.TryCreateSnapshot(json.RootElement);

        Assert.NotNull(result);
        Assert.Null(result.Wood);
        Assert.Null(result.Clay);
        Assert.Null(result.Iron);
        Assert.Equal(75, result.Crop);
    }

    [Fact]
    public void ReturnsNullForNonResourcePayloads()
    {
        using var json = JsonDocument.Parse("""{"player":{"name":"Player"},"production":{"wood":12}}""");

        Assert.Null(GamePayloadParser.TryCreateSnapshot(json.RootElement));
    }

    [Fact]
    public void HandlesEmptyAndNonObjectJson()
    {
        using var empty = JsonDocument.Parse("{}");
        using var array = JsonDocument.Parse("""[{"wood":5}]""");

        Assert.Null(GamePayloadParser.TryCreateSnapshot(empty.RootElement));
        Assert.Null(GamePayloadParser.TryCreateSnapshot(array.RootElement));
    }
}