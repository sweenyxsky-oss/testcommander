using Microsoft.Data.Sqlite;
using TestCommander.Core.Models;
using TestCommander.Models;

namespace TestCommander.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;

    public DatabaseService(string? databasePath = null)
    {
        var usePooling = string.IsNullOrWhiteSpace(databasePath);
        if (usePooling)
        {
            AppPaths.EnsureCreated();
            databasePath = AppPaths.Database;
        }
        else
        {
            databasePath = Path.GetFullPath(databasePath);
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared, Pooling = usePooling,
            DefaultTimeout = 5
        }.ToString();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _initialized)) return;
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _initialized)) return;
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA foreign_keys=ON;
                PRAGMA busy_timeout=5000;
                CREATE TABLE IF NOT EXISTS captured_responses (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, url_path TEXT NOT NULL, content_type TEXT NOT NULL,
                    status_code INTEGER NOT NULL, body_bytes INTEGER NOT NULL, captured_at TEXT NOT NULL, summary TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS village_snapshots (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, village_name TEXT NOT NULL,
                    wood INTEGER NULL, clay INTEGER NULL, iron INTEGER NULL, crop INTEGER NULL,
                    warehouse_capacity INTEGER NULL, granary_capacity INTEGER NULL, captured_at TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS queue_observations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, queue_type TEXT NOT NULL, item_name TEXT NOT NULL,
                    level INTEGER NULL, completes_at TEXT NULL, observed_at TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS troop_movement_observations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, movement_type TEXT NOT NULL, origin TEXT NOT NULL,
                    destination TEXT NOT NULL, arrives_at TEXT NULL, description TEXT NOT NULL, observed_at TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_response_time ON captured_responses(captured_at DESC);
                CREATE INDEX IF NOT EXISTS ix_snapshot_village_time ON village_snapshots(village_name, captured_at DESC);
                CREATE INDEX IF NOT EXISTS ix_queue_time ON queue_observations(observed_at DESC);
                CREATE INDEX IF NOT EXISTS ix_movement_time ON troop_movement_observations(observed_at DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            Volatile.Write(ref _initialized, true);
        }
        finally { _initializationGate.Release(); }
    }

    public async Task SaveResponseAsync(CapturedResponse item, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO captured_responses(url_path,content_type,status_code,body_bytes,captured_at,summary)
            VALUES($url,$type,$status,$bytes,$at,$summary);
            DELETE FROM captured_responses WHERE id NOT IN (SELECT id FROM captured_responses ORDER BY id DESC LIMIT 2000);
            """;
        command.Parameters.AddWithValue("$url", item.UrlPath);
        command.Parameters.AddWithValue("$type", item.ContentType);
        command.Parameters.AddWithValue("$status", item.StatusCode);
        command.Parameters.AddWithValue("$bytes", item.BodyBytes);
        command.Parameters.AddWithValue("$at", item.CapturedAt.ToString("O"));
        command.Parameters.AddWithValue("$summary", item.Summary);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveSnapshotAsync(VillageSnapshot item, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO village_snapshots(village_name,wood,clay,iron,crop,warehouse_capacity,granary_capacity,captured_at)
            SELECT $name,$wood,$clay,$iron,$crop,$warehouse,$granary,$at
            WHERE NOT EXISTS (
                SELECT 1 FROM village_snapshots
                WHERE village_name=$name AND wood IS $wood AND clay IS $clay AND iron IS $iron
                  AND crop IS $crop AND warehouse_capacity IS $warehouse
                  AND granary_capacity IS $granary AND captured_at >= $cutoff
            );
            DELETE FROM village_snapshots WHERE id NOT IN (SELECT id FROM village_snapshots ORDER BY id DESC LIMIT 10000);
            """;
        command.Parameters.AddWithValue("$name", item.VillageName);
        AddNullable(command, "$wood", item.Wood); AddNullable(command, "$clay", item.Clay);
        AddNullable(command, "$iron", item.Iron); AddNullable(command, "$crop", item.Crop);
        AddNullable(command, "$warehouse", item.WarehouseCapacity); AddNullable(command, "$granary", item.GranaryCapacity);
        command.Parameters.AddWithValue("$at", item.CapturedAt.ToString("O"));
        command.Parameters.AddWithValue("$cutoff", item.CapturedAt.AddSeconds(-30).ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveObservationsAsync(IReadOnlyList<QueueObservation> queues,
        IReadOnlyList<TroopMovementObservation> movements, DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        if (queues.Count == 0 && movements.Count == 0) return;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var queue in queues)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO queue_observations(queue_type,item_name,level,completes_at,observed_at)
                SELECT $type,$name,$level,$complete,$observed
                WHERE NOT EXISTS (
                    SELECT 1 FROM queue_observations
                    WHERE queue_type=$type AND item_name=$name AND level IS $level
                      AND completes_at IS $complete AND observed_at >= $cutoff
                )
                """;
            command.Parameters.AddWithValue("$type", queue.QueueType);
            command.Parameters.AddWithValue("$name", queue.ItemName);
            AddNullable(command, "$level", queue.Level);
            command.Parameters.AddWithValue("$complete", queue.CompletesAt?.ToString("O") is { } complete ? complete : DBNull.Value);
            command.Parameters.AddWithValue("$observed", observedAt.ToString("O"));
            command.Parameters.AddWithValue("$cutoff", observedAt.AddSeconds(-30).ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var movement in movements)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO troop_movement_observations(movement_type,origin,destination,arrives_at,description,observed_at)
                SELECT $type,$origin,$destination,$arrival,$description,$observed
                WHERE NOT EXISTS (
                    SELECT 1 FROM troop_movement_observations
                    WHERE movement_type=$type AND origin=$origin AND destination=$destination
                      AND arrives_at IS $arrival AND description=$description AND observed_at >= $cutoff
                )
                """;
            command.Parameters.AddWithValue("$type", movement.MovementType);
            command.Parameters.AddWithValue("$origin", movement.Origin);
            command.Parameters.AddWithValue("$destination", movement.Destination);
            command.Parameters.AddWithValue("$arrival", movement.ArrivesAt?.ToString("O") is { } arrival ? arrival : DBNull.Value);
            command.Parameters.AddWithValue("$description", movement.Description);
            command.Parameters.AddWithValue("$observed", observedAt.ToString("O"));
            command.Parameters.AddWithValue("$cutoff", observedAt.AddSeconds(-30).ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var trim = connection.CreateCommand();
        trim.Transaction = (SqliteTransaction)transaction;
        trim.CommandText = """
            DELETE FROM queue_observations WHERE id NOT IN (SELECT id FROM queue_observations ORDER BY id DESC LIMIT 2000);
            DELETE FROM troop_movement_observations WHERE id NOT IN (SELECT id FROM troop_movement_observations ORDER BY id DESC LIMIT 2000);
            """;
        await trim.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CapturedResponse>> GetRecentResponsesAsync(int limit = 30, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT url_path,content_type,status_code,body_bytes,captured_at,summary FROM captured_responses ORDER BY id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        var results = new List<CapturedResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (DateTimeOffset.TryParse(reader.GetString(4), out var captured))
                results.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), captured, reader.GetString(5)));
        return results;
    }

    public async Task<IReadOnlyList<VillageSnapshot>> GetLatestSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.village_name,s.wood,s.clay,s.iron,s.crop,s.warehouse_capacity,s.granary_capacity,s.captured_at
            FROM village_snapshots s
            INNER JOIN (SELECT village_name,MAX(id) AS max_id FROM village_snapshots GROUP BY village_name) latest ON latest.max_id=s.id
            ORDER BY s.village_name
            """;
        var results = new List<VillageSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (DateTimeOffset.TryParse(reader.GetString(7), out var captured))
                results.Add(new(reader.GetString(0), ReadNullableInt(reader, 1), ReadNullableInt(reader, 2), ReadNullableInt(reader, 3), ReadNullableInt(reader, 4), ReadNullableInt(reader, 5), ReadNullableInt(reader, 6), captured));
        return results;
    }

    public Task<IReadOnlyList<string>> GetRecentQueueSummariesAsync(CancellationToken cancellationToken = default) =>
        QueryStringsAsync("""
            SELECT queue_type || ': ' || item_name ||
                CASE WHEN level IS NOT NULL THEN ' (Lv ' || level || ')' ELSE '' END ||
                CASE WHEN completes_at IS NOT NULL THEN ' • ' || completes_at ELSE '' END
            FROM queue_observations ORDER BY id DESC LIMIT 4
            """, cancellationToken);

    public Task<IReadOnlyList<string>> GetRecentMovementSummariesAsync(CancellationToken cancellationToken = default) =>
        QueryStringsAsync("""
            SELECT movement_type || ': ' || COALESCE(NULLIF(origin, ''), '?') || ' → ' ||
                COALESCE(NULLIF(destination, ''), '?') ||
                CASE WHEN arrives_at IS NOT NULL THEN ' • ' || arrives_at ELSE '' END
            FROM troop_movement_observations ORDER BY id DESC LIMIT 4
            """, cancellationToken);

    private async Task<IReadOnlyList<string>> QueryStringsAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (!reader.IsDBNull(0)) results.Add(reader.GetString(0));
        return results;
    }

    private static int? ReadNullableInt(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
    private static void AddNullable(SqliteCommand command, string name, int? value) =>
        command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value.Value);

    public async Task ClearObservedDataAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            DELETE FROM captured_responses;
            DELETE FROM village_snapshots;
            DELETE FROM queue_observations;
            DELETE FROM troop_movement_observations;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
