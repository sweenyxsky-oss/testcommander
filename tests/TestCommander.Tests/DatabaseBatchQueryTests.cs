using TestCommander.Models;
using TestCommander.Services;
using TestCommander.ViewModels;
using Xunit;

namespace TestCommander.Tests;

public sealed class DatabaseBatchQueryTests
{
    [Fact]
    public async Task ReturnsRecentSnapshotsPartitionedByVillageInChronologicalOrder()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);
        var now = DateTimeOffset.UtcNow;

        // Village Alpha: 5 snapshots (wood: 100, 110, 120, 130, 140)
        for (var i = 0; i < 5; i++)
        {
            await service.SaveSnapshotAsync(new(
                "Village Alpha", 100 + i * 10, 100, 100, 100, 1000, 1000, now.AddMinutes(i)));
        }

        // Village Beta: 2 snapshots (wood: 200, 210)
        for (var i = 0; i < 2; i++)
        {
            await service.SaveSnapshotAsync(new(
                "Village Beta", 200 + i * 10, 200, 200, 200, 1000, 1000, now.AddMinutes(i)));
        }

        // Fetch up to 3 recent snapshots per village
        var batch = await service.GetRecentSnapshotsBatchAsync(limit: 3);

        Assert.Equal(2, batch.Count);
        Assert.True(batch.ContainsKey("Village Alpha"));
        Assert.True(batch.ContainsKey("Village Beta"));

        // Village Alpha had 5; limit 3 should keep the 3 newest (wood 120, 130, 140) in chronological order
        var alphaHistory = batch["Village Alpha"];
        Assert.Equal(3, alphaHistory.Count);
        Assert.Equal(120, alphaHistory[0].Wood);
        Assert.Equal(130, alphaHistory[1].Wood);
        Assert.Equal(140, alphaHistory[2].Wood);

        // Village Beta had 2; should return both (wood 200, 210) in chronological order
        var betaHistory = batch["Village Beta"];
        Assert.Equal(2, betaHistory.Count);
        Assert.Equal(200, betaHistory[0].Wood);
        Assert.Equal(210, betaHistory[1].Wood);
    }

    [Fact]
    public async Task RefreshAsyncPopulatesOverviewsUsingBatchHistory()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);
        var viewModel = new MainViewModel(service);
        var now = DateTimeOffset.UtcNow;

        // Populate multiple villages with distinct samples
        await service.SaveSnapshotAsync(new("Village 1", 100, 100, 100, 100, 1000, 1000, now.AddMinutes(-5)));
        await service.SaveSnapshotAsync(new("Village 1", 150, 120, 110, 100, 1000, 1000, now));

        await service.SaveSnapshotAsync(new("Village 2", 300, 300, 300, 300, 2000, 2000, now.AddMinutes(-10)));
        await service.SaveSnapshotAsync(new("Village 2", 450, 400, 350, 300, 2000, 2000, now));

        await viewModel.RefreshAsync();

        Assert.Equal(2, viewModel.ResourceOverviews.Count);
        Assert.Equal(2, viewModel.VillageComparisons.Count);

        var v1Overview = Assert.Single(viewModel.ResourceOverviews, o => o.VillageName == "Village 1");
        Assert.Equal(150, v1Overview.Resources[0].Stock);
        var v1WoodChange = Assert.Single(v1Overview.Changes, c => c.Name == "Wood");
        Assert.Equal(50, v1WoodChange.Delta);

        var v2Overview = Assert.Single(viewModel.ResourceOverviews, o => o.VillageName == "Village 2");
        Assert.Equal(450, v2Overview.Resources[0].Stock);
        var v2WoodChange = Assert.Single(v2Overview.Changes, c => c.Name == "Wood");
        Assert.Equal(150, v2WoodChange.Delta);

        Assert.Contains("2 village(s)", viewModel.ResourceSummary);
    }

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
