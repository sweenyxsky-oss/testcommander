using TestCommander.Models;
using TestCommander.Services;
using Xunit;

namespace TestCommander.Tests;

public sealed class DatabaseRetentionTests
{
    [Fact]
    public async Task DefaultsToSixMonthsAndPersistsTheSelectedRetention()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);

        Assert.Equal(180, await service.GetSnapshotRetentionDaysAsync());

        await service.SetSnapshotRetentionDaysAsync(90);
        Assert.Equal(90, await service.GetSnapshotRetentionDaysAsync());

        await service.SetSnapshotRetentionDaysAsync(null);
        Assert.Null(await service.GetSnapshotRetentionDaysAsync());
    }

    [Fact]
    public async Task PrunesExpiredSnapshotsButKeepsRecentHistory()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);
        var now = DateTimeOffset.UtcNow;

        await service.SaveSnapshotAsync(Snapshot("Test village", 100, now.AddDays(-31)));
        await service.SaveSnapshotAsync(Snapshot("Test village", 200, now.AddDays(-2)));

        var removed = await service.PruneSnapshotsAsync(30);
        var remaining = await service.GetRecentSnapshotsAsync("Test village", 10);

        Assert.Equal(1, removed);
        var snapshot = Assert.Single(remaining);
        Assert.Equal(200, snapshot.Wood);
    }

    [Fact]
    public async Task NeverRetentionDoesNotDeleteOldSnapshots()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);

        await service.SaveSnapshotAsync(Snapshot("Old village", 100, DateTimeOffset.UtcNow.AddYears(-5)));

        Assert.Equal(0, await service.PruneSnapshotsAsync(null));
        Assert.Single(await service.GetRecentSnapshotsAsync("Old village", 10));
    }

    [Fact]
    public async Task RejectsInvalidRetentionValues()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetSnapshotRetentionDaysAsync(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetSnapshotRetentionDaysAsync(3651));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.PruneSnapshotsAsync(-1));
    }

    private static VillageSnapshot Snapshot(string name, int wood, DateTimeOffset capturedAt) =>
        new(name, wood, 100, 100, 100, 1000, 1000, capturedAt);

    private sealed class TemporaryDatabase : IDisposable
    {
        public string DirectoryPath { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "TestCommander.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(DirectoryPath, "test.db");

        public void Dispose()
        {
            if (System.IO.Directory.Exists(DirectoryPath))
                System.IO.Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}