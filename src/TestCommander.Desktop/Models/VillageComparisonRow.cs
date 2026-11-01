namespace TestCommander.Models;

/// <summary>A compact, read-only comparison row derived only from observed village snapshots.</summary>
public sealed record VillageComparisonRow(
    string VillageName,
    string Wood,
    string Clay,
    string Iron,
    string Crop,
    DateTimeOffset CapturedAt);
