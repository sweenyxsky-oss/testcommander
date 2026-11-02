using Microsoft.Data.Sqlite;
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

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task RefreshAsyncMaintainsConstantHistoryReadsRegardlessOfVillageCount(int villageCount)
    {
        using var database = new TemporaryDatabase();
        var service = new CountingDatabaseService(database.Path);
        var viewModel = new MainViewModel(service);
        var now = DateTimeOffset.UtcNow;

        for (var i = 1; i <= villageCount; i++)
        {
            var villageName = $"Village {i}";
            await service.SaveSnapshotAsync(new(villageName, 100, 100, 100, 100, 1000, 1000, now.AddMinutes(-5)));
            await service.SaveSnapshotAsync(new(villageName, 150, 120, 110, 100, 1000, 1000, now));
        }

        await viewModel.RefreshAsync();

        Assert.Equal(villageCount, viewModel.ResourceOverviews.Count);
        // History query count remains strictly 1 regardless of whether village count is 1, 5, or 20
        Assert.Equal(1, service.BatchHistoryCallCount);
    }

    [Fact]
    public async Task GetRecentSnapshotsBatchAsyncReturnsEmptyOnEmptyDatabase()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);

        var batch = await service.GetRecentSnapshotsBatchAsync(limit: 12);

        Assert.NotNull(batch);
        Assert.Empty(batch);
    }

    [Fact]
    public async Task GetRecentSnapshotsBatchAsyncReturnsEmptyWhenLimitIsZero()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);
        await service.SaveSnapshotAsync(new("Village Alpha", 100, 100, 100, 100, 1000, 1000, DateTimeOffset.UtcNow));

        var batch = await service.GetRecentSnapshotsBatchAsync(limit: 0);

        Assert.NotNull(batch);
        Assert.Empty(batch);
    }

    [Fact]
    public async Task GetRecentSnapshotsBatchAsyncReturnsSingleLatestSnapshotWhenLimitIsOne()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);
        var now = DateTimeOffset.UtcNow;

        await service.SaveSnapshotAsync(new("Village Alpha", 100, 100, 100, 100, 1000, 1000, now.AddMinutes(-5)));
        await service.SaveSnapshotAsync(new("Village Alpha", 150, 120, 110, 100, 1000, 1000, now));

        await service.SaveSnapshotAsync(new("Village Beta", 200, 200, 200, 200, 1000, 1000, now.AddMinutes(-5)));
        await service.SaveSnapshotAsync(new("Village Beta", 250, 220, 210, 200, 1000, 1000, now));

        var batch = await service.GetRecentSnapshotsBatchAsync(limit: 1);

        Assert.Equal(2, batch.Count);

        var alphaHistory = batch["Village Alpha"];
        Assert.Single(alphaHistory);
        Assert.Equal(150, alphaHistory[0].Wood);

        var betaHistory = batch["Village Beta"];
        Assert.Single(betaHistory);
        Assert.Equal(250, betaHistory[0].Wood);
    }

    [Fact]
    public async Task BatchQueryUsesVillageNameIdIndexAccordingToQueryPlan()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);
        // Ensure schema and tables/indexes are created
        await service.SaveSnapshotAsync(new("Village 1", 100, 100, 100, 100, 1000, 1000, DateTimeOffset.UtcNow));

        var queryPlanLines = new List<string>();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database.Path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                EXPLAIN QUERY PLAN
                WITH RankedSnapshots AS (
                    SELECT id, village_name, wood, clay, iron, crop, 
                           warehouse_capacity, granary_capacity, captured_at,
                           ROW_NUMBER() OVER (
                               PARTITION BY village_name 
                               ORDER BY id DESC
                           ) AS rank_index
                    FROM village_snapshots
                )
                SELECT village_name, wood, clay, iron, crop, 
                       warehouse_capacity, granary_capacity, captured_at
                FROM RankedSnapshots
                WHERE rank_index <= 12
                ORDER BY village_name, id ASC;
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var detail = reader.GetString(reader.GetOrdinal("detail"));
                queryPlanLines.Add(detail);
            }
        }

        SqliteConnection.ClearAllPools();
        var fullPlan = string.Join("\n", queryPlanLines);
        Assert.Contains("USING INDEX ix_snapshot_village_id", fullPlan);
    }

    private sealed class CountingDatabaseService(string? databasePath = null) : DatabaseService(databasePath)
    {
        public int BatchHistoryCallCount { get; private set; }

        public override Task<IReadOnlyDictionary<string, IReadOnlyList<VillageSnapshot>>> GetRecentSnapshotsBatchAsync(
            int limit = 12, CancellationToken cancellationToken = default)
        {
            BatchHistoryCallCount++;
            return base.GetRecentSnapshotsBatchAsync(limit, cancellationToken);
        }
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public string DirectoryPath { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "TestCommander.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(DirectoryPath, "test.db");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (System.IO.Directory.Exists(DirectoryPath))
                    System.IO.Directory.Delete(DirectoryPath, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
