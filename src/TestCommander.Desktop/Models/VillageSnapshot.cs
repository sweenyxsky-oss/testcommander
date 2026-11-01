using TestCommander.Core.Models;

namespace TestCommander.Models;

public sealed record VillageSnapshot(
    string VillageName,
    int? Wood,
    int? Clay,
    int? Iron,
    int? Crop,
    int? WarehouseCapacity,
    int? GranaryCapacity,
    DateTimeOffset CapturedAt)
{
    public static VillageSnapshot From(ResourceSnapshot snapshot, DateTimeOffset capturedAt) =>
        new(snapshot.VillageName, snapshot.Wood, snapshot.Clay, snapshot.Iron, snapshot.Crop,
            snapshot.WarehouseCapacity, snapshot.GranaryCapacity, capturedAt);
}
