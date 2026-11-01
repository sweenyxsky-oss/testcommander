using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using TestCommander.Models;

namespace TestCommander.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _snapshotWriteGate = new(1, 1);
    private bool _initialized;

    public DatabaseService(string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            AppPaths.EnsureCreated();
            databasePath = AppPaths.Database;
        }
        else
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = databasePath == AppPaths.Database,
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
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA foreign_keys=ON;
                PRAGMA busy_timeout=5000;
                CREATE TABLE IF NOT EXISTS captured_responses (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    url_path TEXT NOT NULL,
                    content_type TEXT NOT NULL,
                    status_code INTEGER NOT NULL,
                    body_bytes INTEGER NOT NULL,
                    captured_at TEXT NOT NULL,
                    summary TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS village_snapshots (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    village_name TEXT NOT NULL,
                    wood INTEGER NULL, clay INTEGER NULL, iron INTEGER NULL, crop INTEGER NULL,
                    warehouse_capacity INTEGER NULL, granary_capacity INTEGER NULL,
                    captured_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS app_settings (
                    setting_key TEXT PRIMARY KEY,
                    setting_value TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_response_time ON captured_responses(captured_at DESC);
                CREATE INDEX IF NOT EXISTS ix_snapshot_village_time ON village_snapshots(village_name, captured_at DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task SaveResponseAsync(CapturedResponse item, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO captured_responses(url_path,content_type,status_code,body_bytes,captured_at,summary) VALUES($url,$type,$status,$bytes,$at,$summary)";
        command.Parameters.AddWithValue("$url", item.UrlPath);
        command.Parameters.AddWithValue("$type", item.ContentType);
        command.Parameters.AddWithValue("$status", item.StatusCode);
        command.Parameters.AddWithValue("$bytes", item.BodyBytes);
        command.Parameters.AddWithValue("$at", item.CapturedAt.ToString("O"));
        command.Parameters.AddWithValue("$summary", item.Summary);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> SaveSnapshotAsync(VillageSnapshot item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await _snapshotWriteGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using (var latest = connection.CreateCommand())
            {
                latest.CommandText = """
                    SELECT wood,clay,iron,crop,warehouse_capacity,granary_capacity
                    FROM village_snapshots
                    WHERE village_name=$name
                    ORDER BY id DESC LIMIT 1
                    """;
                latest.Parameters.AddWithValue("$name", item.VillageName);
                await using var reader = await latest.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken) &&
                    ReadNullableInt(reader, 0) == item.Wood &&
                    ReadNullableInt(reader, 1) == item.Clay &&
                    ReadNullableInt(reader, 2) == item.Iron &&
                    ReadNullableInt(reader, 3) == item.Crop &&
                    ReadNullableInt(reader, 4) == item.WarehouseCapacity &&
                    ReadNullableInt(reader, 5) == item.GranaryCapacity)
                    return false;
            }

            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO village_snapshots(village_name,wood,clay,iron,crop,warehouse_capacity,granary_capacity,captured_at)
                VALUES($name,$wood,$clay,$iron,$crop,$warehouse,$granary,$at)
                """;
            insert.Parameters.AddWithValue("$name", item.VillageName);
            AddNullable(insert, "$wood", item.Wood);
            AddNullable(insert, "$clay", item.Clay);
            AddNullable(insert, "$iron", item.Iron);
            AddNullable(insert, "$crop", item.Crop);
            AddNullable(insert, "$warehouse", item.WarehouseCapacity);
            AddNullable(insert, "$granary", item.GranaryCapacity);
            insert.Parameters.AddWithValue("$at", item.CapturedAt.ToUniversalTime().ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        finally
        {
            _snapshotWriteGate.Release();
        }
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
        {
            if (!DateTimeOffset.TryParse(reader.GetString(4), out var captured)) continue;
            results.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), captured, reader.GetString(5)));
        }
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
        {
            if (!DateTimeOffset.TryParse(reader.GetString(7), out var captured)) continue;
            results.Add(ReadSnapshot(reader, captured));
        }
        return results;
    }

    public async Task<IReadOnlyList<VillageSnapshot>> GetRecentSnapshotsAsync(
        string villageName, int limit = 2, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(villageName))
            throw new ArgumentException("A village name is required.", nameof(villageName));

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT village_name,wood,clay,iron,crop,warehouse_capacity,granary_capacity,captured_at
            FROM village_snapshots
            WHERE village_name=$name
            ORDER BY id DESC LIMIT $limit
            """;
        command.Parameters.AddWithValue("$name", villageName);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 2, 500));
        var results = new List<VillageSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!DateTimeOffset.TryParse(reader.GetString(7), out var captured)) continue;
            results.Add(ReadSnapshot(reader, captured));
        }
        results.Reverse();
        return results;
    }

    public async Task<int?> GetSnapshotRetentionDaysAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT setting_value FROM app_settings WHERE setting_key='snapshot_retention_days' LIMIT 1";
            var value = await read.ExecuteScalarAsync(cancellationToken);
            if (value is string stored)
                return string.Equals(stored, "never", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : int.TryParse(stored, out var days) && days > 0 ? days : 180;
        }

        await SetSnapshotRetentionDaysAsync(180, cancellationToken);
        return 180;
    }

    public async Task SetSnapshotRetentionDaysAsync(int? days, CancellationToken cancellationToken = default)
    {
        if (days is <= 0 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(days), "Retention must be between 1 and 3650 days, or null for never.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings(setting_key,setting_value)
            VALUES('snapshot_retention_days',$value)
            ON CONFLICT(setting_key) DO UPDATE SET setting_value=excluded.setting_value
            """;
        command.Parameters.AddWithValue("$value", days?.ToString() ?? "never");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> PruneSnapshotsAsync(int? retentionDays, CancellationToken cancellationToken = default)
    {
        if (retentionDays is null) return 0;
        if (retentionDays <= 0 || retentionDays > 3650)
            throw new ArgumentOutOfRangeException(nameof(retentionDays));

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays.Value).ToString("O");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM village_snapshots WHERE captured_at < $cutoff";
        command.Parameters.AddWithValue("$cutoff", cutoff);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ExportSnapshotsCsvAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT village_name,wood,clay,iron,crop,warehouse_capacity,granary_capacity,captured_at
            FROM village_snapshots
            ORDER BY village_name,captured_at,id
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await using var writer = new StreamWriter(
            destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 4096, leaveOpen: true);

        await writer.WriteLineAsync("VillageName,CapturedAtUtc,Wood,Clay,Iron,Crop,WarehouseCapacity,GranaryCapacity");

        while (await reader.ReadAsync(cancellationToken))
        {
            var fields = new string?[]
            {
                EscapeCsv(reader.GetString(0), protectFormula: true),
                EscapeCsv(reader.GetString(7)),
                EscapeCsv(ReadNullableInt(reader, 1)?.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(ReadNullableInt(reader, 2)?.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(ReadNullableInt(reader, 3)?.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(ReadNullableInt(reader, 4)?.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(ReadNullableInt(reader, 5)?.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(ReadNullableInt(reader, 6)?.ToString(CultureInfo.InvariantCulture))
            };
            await writer.WriteLineAsync(string.Join(",", fields));
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static string EscapeCsv(string? value, bool protectFormula = false)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        if (protectFormula && value[0] is '=' or '+' or '-' or '@' or '\\t' or '\\r')
            value = "'" + value;

        if (value.IndexOfAny([',', '"', '\\r', '\\n']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static VillageSnapshot ReadSnapshot(SqliteDataReader reader, DateTimeOffset captured) =>
        new(reader.GetString(0), ReadNullableInt(reader, 1), ReadNullableInt(reader, 2),
            ReadNullableInt(reader, 3), ReadNullableInt(reader, 4), ReadNullableInt(reader, 5),
            ReadNullableInt(reader, 6), captured);

    private static int? ReadNullableInt(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
    private static void AddNullable(SqliteCommand command, string name, int? value) =>
        command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value.Value);
}