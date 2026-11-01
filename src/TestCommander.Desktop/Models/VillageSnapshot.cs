namespace TestCommander.Models;

public sealed record VillageSnapshot(
    string VillageName,
    int? Wood,
    int? Clay,
    int? Iron,
    int? Crop,
    int? WarehouseCapacity,
    int? GranaryCapacity,
    DateTimeOffset CapturedAt);