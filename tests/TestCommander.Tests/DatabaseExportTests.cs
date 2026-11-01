using System.Text;
using TestCommander.Models;
using TestCommander.Services;
using Xunit;

namespace TestCommander.Tests;

public sealed class DatabaseExportTests
{
    [Fact]
    public async Task ExportsHistoryAsUtf8CsvAndEscapesNamesSafely()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);
        var timestamp = DateTimeOffset.UtcNow;

        await service.SaveSnapshotAsync(new VillageSnapshot(
            "=1+1", null, 20, 30, 40, 100, 100, timestamp));
        await service.SaveSnapshotAsync(new VillageSnapshot(
            "Village, \"North\"", 10, 20, 30, 40, 100, 100, timestamp.AddMinutes(1)));

        await using var stream = new MemoryStream();
        await service.ExportSnapshotsCsvAsync(stream);

        Assert.True(stream.CanWrite);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var csv = await reader.ReadToEndAsync();

        Assert.StartsWith("VillageName,CapturedAtUtc,Wood,Clay,Iron,Crop,WarehouseCapacity,GranaryCapacity", csv);
        Assert.Contains("'=1+1,", csv);
        Assert.Contains("Village, \"\"North\"\"", csv);
        Assert.Contains(",,20,30,40,100,100", csv);
        Assert.Equal(3, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task ExportsHeaderWhenThereAreNoSnapshots()
    {
        using var database = new TemporaryDatabase();
        var service = new DatabaseService(database.Path);
        await using var stream = new MemoryStream();

        await service.ExportSnapshotsCsvAsync(stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var csv = await reader.ReadToEndAsync();

        Assert.Equal("VillageName,CapturedAtUtc,Wood,Clay,Iron,Crop,WarehouseCapacity,GranaryCapacity" + Environment.NewLine, csv);
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public string DirectoryPath { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "TestCommander.Export.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(DirectoryPath, "test.db");

        public void Dispose()
        {
            if (System.IO.Directory.Exists(DirectoryPath))
                System.IO.Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}