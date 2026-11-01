namespace TestCommander.Models;

public sealed record VillageResourceOverview(
    string VillageName,
    DateTimeOffset CapturedAt,
    IReadOnlyList<ResourceGauge> Resources,
    IReadOnlyList<ResourceChange> Changes,
    IReadOnlyList<ResourceTrend> Trends)
{
    public string CapturedAtLabel => $"Captured {CapturedAt.ToLocalTime():g}";

    public string HistorySummary => Changes.Count == 0
        ? "Waiting for a second sample with comparable resource values"
        : "Since previous distinct sample: " + string.Join(" • ", Changes.Select(change => change.DisplayLabel));
}