using System.IO;
using TestCommander.Models;
using TestCommander.Services;
using Xunit;

namespace TestCommander.Tests;

public sealed class DatabaseBackupRestoreTests
{
    [Fact]
    public async Task BackupIsConsistentAndCanBeValidated()
    {
        using var files = new TemporaryFiles();
        var service = new DatabaseService(files.LivePath);
        await service.SaveSnapshotAsync(Snapshot("Original village", 1234));
        await service.SetSnapshotRetentionDaysAsync(90);

        var backup = await service.CreateBackupAsync(files.BackupPath);
        await service.ValidateBackupAsync(backup);

        Assert.True(File.Exists(backup));
        Assert.True(new FileInfo(backup).Length > 0);

        var restoredView = new DatabaseService(backup);
        var snapshot = Assert.Single(await restoredView.GetRecentSnapshotsAsync("Original village"));
        Assert.Equal(1234, snapshot.Wood);
        Assert.Equal(90, await restoredView.GetSnapshotRetentionDaysAsync());
    }

    [Fact]
    public async Task RestoreReplacesDataAndCreatesSafetyBackupFirst()
    {
        using var files = new TemporaryFiles();
        var service = new DatabaseService(files.LivePath);
        await service.SaveSnapshotAsync(Snapshot("Current village", 100));
        var backupService = new DatabaseService(files.SourcePath);
        await backupService.SaveSnapshotAsync(Snapshot("Backup village", 900));
        await backupService.SetSnapshotRetentionDaysAsync(30);
        var backup = await backupService.CreateBackupAsync(files.BackupPath);

        var safetyBackup = await service.RestoreBackupAsync(backup);

        Assert.True(File.Exists(safetyBackup));
        await service.ValidateBackupAsync(safetyBackup);
        Assert.Empty(await service.GetRecentSnapshotsAsync("Current village"));
        var restored = Assert.Single(await service.GetRecentSnapshotsAsync("Backup village"));
        Assert.Equal(900, restored.Wood);
        Assert.Equal(30, await service.GetSnapshotRetentionDaysAsync());

        var safetyView = new DatabaseService(safetyBackup);
        var original = Assert.Single(await safetyView.GetRecentSnapshotsAsync("Current village"));
        Assert.Equal(100, original.Wood);
    }

    [Fact]
    public async Task InvalidBackupIsRejectedWithoutChangingLiveDatabase()
    {
        using var files = new TemporaryFiles();
        var service = new DatabaseService(files.LivePath);
        await service.SaveSnapshotAsync(Snapshot("Keep this village", 777));
        await File.WriteAllTextAsync(files.BackupPath, "not a SQLite database");

        await Assert.ThrowsAsync<Exception>(() => service.RestoreBackupAsync(files.BackupPath));

        var retained = Assert.Single(await service.GetRecentSnapshotsAsync("Keep this village"));
        Assert.Equal(777, retained.Wood);
    }

    private static VillageSnapshot Snapshot(string name, int wood) =>
        new(name, wood, 200, 300, 400, 1000, 1000, DateTimeOffset.UtcNow);

    private sealed class TemporaryFiles : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(
            Path.GetTempPath(), "TestCommander.Backup.Tests", Guid.NewGuid().ToString("N"));
        public string LivePath => Path.Combine(DirectoryPath, "live.db");
        public string SourcePath => Path.Combine(DirectoryPath, "source.db");
        public string BackupPath => Path.Combine(DirectoryPath, "backup.db");

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
