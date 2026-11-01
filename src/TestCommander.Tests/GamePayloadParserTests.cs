using TestCommander.Core.Parsing;

namespace TestCommander.Tests;

public sealed class GamePayloadParserTests
{
    [Fact]
    public void ParsesResourcesFromDedicatedResourceObject()
    {
        const string json = """
            {"villageName":"Capital","resources":{"wood":1200,"clay":"900","iron":750,"crop":600,"warehouseCapacity":5000,"granaryCapacity":4000}}
            """;

        var result = GamePayloadParser.Parse(json);

        Assert.NotNull(result.Resources);
        Assert.Equal("Capital", result.Resources.VillageName);
        Assert.Equal(1200, result.Resources.Wood);
        Assert.Equal(900, result.Resources.Clay);
        Assert.Equal(750, result.Resources.Iron);
        Assert.Equal(600, result.Resources.Crop);
        Assert.Equal(5000, result.Resources.WarehouseCapacity);
        Assert.Equal(4000, result.Resources.GranaryCapacity);
    }

    [Fact]
    public void DoesNotMistakeUnrelatedNestedNumbersForResources()
    {
        const string json = """{"player":{"wood":100},"statistics":{"clay":200},"inventory":{"iron":300}}""";
        var result = GamePayloadParser.Parse(json);
        Assert.Null(result.Resources);
    }

    [Fact]
    public void ParsesBuildingAndTrainingQueues()
    {
        const string json = """
            {"buildingQueue":[{"buildingName":"Granary","level":12,"completesAt":"2026-11-01T20:00:00Z"}],
             "training_queue":[{"unitName":"Legionnaire","completionTime":"2026-11-01T21:00:00Z"}]}
            """;

        var result = GamePayloadParser.Parse(json);

        Assert.Equal(2, result.Queues.Count);
        Assert.Contains(result.Queues, x => x.ItemName == "Granary" && x.Level == 12);
        Assert.Contains(result.Queues, x => x.ItemName == "Legionnaire");
    }

    [Fact]
    public void ParsesIncomingMovementAndUnixArrival()
    {
        const string json = """
            {"incomingAttacks":[{"origin":"Enemy village","destination":"Capital","type":"Attack","arrivalTime":1793563200}]}
            """;

        var result = GamePayloadParser.Parse(json);

        var movement = Assert.Single(result.TroopMovements);
        Assert.Equal("Enemy village", movement.Origin);
        Assert.Equal("Capital", movement.Destination);
        Assert.Equal("Attack", movement.MovementType);
        Assert.NotNull(movement.ArrivesAt);
    }

    [Fact]
    public void InvalidJsonThrowsJsonException()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => GamePayloadParser.Parse("{invalid"));
    }
}
