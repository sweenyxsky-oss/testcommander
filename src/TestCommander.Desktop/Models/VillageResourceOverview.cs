namespace TestCommander.Models;

public sealed record VillageResourceOverview(
    string VillageName,
    DateTimeOffset CapturedAt,
    IReadOnlyList<ResourceGauge> Resources)
{
    public string CapturedAtLabel => $"Captured {CapturedAt.ToLocalTime():g}";
}