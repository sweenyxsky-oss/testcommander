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
    public async Task BatchQueryHandlesRealisticVillageAndSnapshotCountsWithinBoundedTime()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);
        var start = DateTimeOffset.UtcNow.AddDays(-7);
        const int villageCount = 75;
        const int snapshotsPerVillage = 400;
        const int expectedSamplesPerVillage = 24;

        // Initialize schema, then seed in one transaction so this test measures the batch read.
        await service.GetRecentSnapshotsBatchAsync(limit: 1);
        var seedConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database.Path,
            Pooling = false
        }.ToString();
        await using (var seedConnection = new SqliteConnection(seedConnectionString))
        {
            await seedConnection.OpenAsync();
            await using var transaction = await seedConnection.BeginTransactionAsync();
            await using var insert = seedConnection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO village_snapshots(village_name, wood, clay, iron, crop, warehouse_capacity, granary_capacity, captured_at)
                VALUES($name, $wood, $clay, $iron, $crop, 10000, 10000, $capturedAt)
                """;
            var name = insert.Parameters.Add("$name", SqliteType.Text);
            var wood = insert.Parameters.Add("$wood", SqliteType.Integer);
            var clay = insert.Parameters.Add("$clay", SqliteType.Integer);
            var iron = insert.Parameters.Add("$iron", SqliteType.Integer);
            var crop = insert.Parameters.Add("$crop", SqliteType.Integer);
            var capturedAt = insert.Parameters.Add("$capturedAt", SqliteType.Text);

            for (var village = 0; village < villageCount; village++)
            for (var sample = 0; sample < snapshotsPerVillage; sample++)
            {
                name.Value = $"Village {village:D3}";
                wood.Value = sample;
                clay.Value = sample + 1;
                iron.Value = sample + 2;
                crop.Value = sample + 3;
                capturedAt.Value = start.AddMinutes(village * snapshotsPerVillage + sample).ToString("O");
                await insert.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var batch = await service.GetRecentSnapshotsBatchAsync(expectedSamplesPerVillage);
        stopwatch.Stop();

        Assert.Equal(villageCount, batch.Count);
        Assert.All(batch.Values, history => Assert.Equal(expectedSamplesPerVillage, history.Count));
        Assert.Equal(snapshotsPerVillage - expectedSamplesPerVillage, batch["Village 000"][0].Wood);
        Assert.Equal(snapshotsPerVillage - 1, batch["Village 000"][^1].Wood);
        // Generous ceiling is a regression sentinel, not a microbenchmark. CI variance is expected.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Batch query took {stopwatch.Elapsed}; expected under 10 seconds for {villageCount * snapshotsPerVillage:N0} rows.");
    }

    [Fact]
    public async Task EnsureSchemaAsyncCreatesBatchIndexOnExistingDatabaseWithoutLosingData()
    {
        using var database = new TemporaryDatabase();
        var originalName = "Preserved village";
        var originalCapturedAt = DateTimeOffset.UtcNow.ToString("O");

        // Simulate a database created by an earlier version without the new index.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database.Path,
            Pooling = false
        }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE village_snapshots (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    village_name TEXT NOT NULL,
                    wood INTEGER NULL, clay INTEGER NULL, iron INTEGER NULL, crop INTEGER NULL,
                    warehouse_capacity INTEGER NULL, granary_capacity INTEGER NULL,
                    captured_at TEXT NOT NULL
                );
                INSERT INTO village_snapshots(village_name, wood, clay, iron, crop, warehouse_capacity, granary_capacity, captured_at)
                VALUES($name, 321, 654, 987, 123, 5000, 6000, $capturedAt);
                """;
            command.Parameters.AddWithValue("$name", originalName);
            command.Parameters.AddWithValue("$capturedAt", originalCapturedAt);
            await command.ExecuteNonQueryAsync();
        }

        var service = new DatabaseService(database.Path);
        var batch = await service.GetRecentSnapshotsBatchAsync(12);

        var preserved = Assert.Single(batch[originalName]);
        Assert.Equal(321, preserved.Wood);
        Assert.Equal(originalCapturedAt, preserved.CapturedAt.ToString("O"));

        await using var verification = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database.Path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await verification.OpenAsync();
        await using var indexCheck = verification.CreateCommand();
        indexCheck.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_snapshot_village_id'";
        Assert.Equal(1L, (long)(await indexCheck.ExecuteScalarAsync())!);

        await using var rowCheck = verification.CreateCommand();
        rowCheck.CommandText = "SELECT COUNT(*) FROM village_snapshots";
        Assert.Equal(1L, (long)(await rowCheck.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task RefreshAsyncCoalescesRapidEventsWithoutOverlappingDatabaseReads()
    {
        using var database = new TemporaryDatabase();
        var service = new BlockingDatabaseService(database.Path);
        var viewModel = new MainViewModel(service);

        var firstRefresh = viewModel.RefreshAsync();
        await service.FirstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var queuedRefreshes = Enumerable.Range(0, 20)
            .Select(_ => viewModel.RefreshAsync())
            .ToArray();

        Assert.Equal(1, service.MaximumConcurrentReads);
        service.ReleaseFirstRead.TrySetResult();
        await Task.WhenAll(queuedRefreshes.Append(firstRefresh));

        Assert.Equal(1, service.MaximumConcurrentReads);
        Assert.Equal(2, service.GetRecentResponsesCallCount);
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

    private sealed class BlockingDatabaseService(string? databasePath = null) : DatabaseService(databasePath)
    {
        private int _activeReads;
        private int _maximumConcurrentReads;
        public TaskCompletionSource FirstReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumConcurrentReads => Volatile.Read(ref _maximumConcurrentReads);
        public int GetRecentResponsesCallCount { get; private set; }

        public override async Task<IReadOnlyList<CapturedResponse>> GetRecentResponsesAsync(int limit = 30, CancellationToken cancellationToken = default)
        {
            GetRecentResponsesCallCount++;
            var active = Interlocked.Increment(ref _activeReads);
            UpdateMaximum(active);
            try
            {
                if (GetRecentResponsesCallCount == 1)
                {
                    FirstReadStarted.TrySetResult();
                    await ReleaseFirstRead.Task.WaitAsync(cancellationToken);
                }
                return await base.GetRecentResponsesAsync(limit, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activeReads);
            }
        }

        public override async Task<IReadOnlyList<VillageSnapshot>> GetLatestSnapshotsAsync(CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activeReads);
            UpdateMaximum(active);
            try { return await base.GetLatestSnapshotsAsync(cancellationToken); }
            finally { Interlocked.Decrement(ref _activeReads); }
        }

        public override async Task<IReadOnlyDictionary<string, IReadOnlyList<VillageSnapshot>>> GetRecentSnapshotsBatchAsync(
            int limit = 12, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activeReads);
            UpdateMaximum(active);
            try { return await base.GetRecentSnapshotsBatchAsync(limit, cancellationToken); }
            finally { Interlocked.Decrement(ref _activeReads); }
        }

        private void UpdateMaximum(int current)
        {
            while (true)
            {
                var previous = Volatile.Read(ref _maximumConcurrentReads);
                if (previous >= current || Interlocked.CompareExchange(ref _maximumConcurrentReads, current, previous) == previous)
                    return;
            }
        }
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
