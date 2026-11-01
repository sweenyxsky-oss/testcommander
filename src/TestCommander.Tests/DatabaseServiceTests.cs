using TestCommander.Core.Models;
using TestCommander.Models;
using TestCommander.Services;
using Xunit;

namespace TestCommander.Tests;

public sealed class DatabaseServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly DatabaseService _database;

    public DatabaseServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"TestCommanderTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _database = new DatabaseService(Path.Combine(_directory, "test.db"));
    }

    [Fact]
    public async Task DuplicateSnapshotsWithinThirtySecondsAreNotInsertedAgain()
    {
        var firstAt = DateTimeOffset.UtcNow;
        var first = new VillageSnapshot("Capital", 100, 200, 300, 400, 5000, 6000, firstAt);
        var repeated = first with { CapturedAt = firstAt.AddSeconds(10) };

        await _database.SaveSnapshotAsync(first);
        await _database.SaveSnapshotAsync(repeated);

        var snapshot = Assert.Single(await _database.GetLatestSnapshotsAsync());
        Assert.Equal(firstAt, snapshot.CapturedAt);
        Assert.Equal(100, snapshot.Wood);
        Assert.Equal(5000, snapshot.WarehouseCapacity);
    }

    [Fact]
    public async Task DuplicateQueueAndMovementObservationsAreDeduplicated()
    {
        var firstAt = DateTimeOffset.UtcNow;
        var queue = new QueueObservation("Training Queue", "Legionnaire", 1, firstAt.AddMinutes(2));
        var movement = new TroopMovementObservation("Incoming Attacks", "Enemy", "Capital",
            firstAt.AddMinutes(5), "Attack");

        await _database.SaveObservationsAsync([queue], [movement], firstAt);
        await _database.SaveObservationsAsync([queue], [movement], firstAt.AddSeconds(10));

        Assert.Single(await _database.GetRecentQueueSummariesAsync());
        Assert.Single(await _database.GetRecentMovementSummariesAsync());
    }

    [Fact]
    public async Task ClearObservedDataRemovesAllLocalObservations()
    {
        var now = DateTimeOffset.UtcNow;
        await _database.SaveResponseAsync(new CapturedResponse("/api/village", "application/json",
            200, 42, now, "Resources: yes"));
        await _database.SaveSnapshotAsync(new VillageSnapshot("Capital", 1, 2, 3, 4, 5, 6, now));
        await _database.SaveObservationsAsync(
            [new QueueObservation("Building Queue", "Granary", 10, now.AddMinutes(1))],
            [new TroopMovementObservation("Incoming Attacks", "Enemy", "Capital", now.AddMinutes(3), "Attack")],
            now);

        await _database.ClearObservedDataAsync();

        Assert.Empty(await _database.GetRecentResponsesAsync());
        Assert.Empty(await _database.GetLatestSnapshotsAsync());
        Assert.Empty(await _database.GetRecentQueueSummariesAsync());
        Assert.Empty(await _database.GetRecentMovementSummariesAsync());
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
