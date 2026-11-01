using TestCommander.Models;

namespace TestCommander.Services;

/// <summary>
/// Creates a compact comparison from the latest observed overview for each village.
/// Missing stocks and capacities remain explicit; no production is inferred.
/// </summary>
public static class VillageComparisonBuilder
{
    public static IReadOnlyList<VillageComparisonRow> Create(IEnumerable<VillageResourceOverview> overviews)
    {
        ArgumentNullException.ThrowIfNull(overviews);

        return overviews
            .Where(overview => overview is not null)
            .OrderBy(overview => overview.VillageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(overview => overview.VillageName, StringComparer.Ordinal)
            .Select(CreateRow)
            .ToArray();
    }

    private static VillageComparisonRow CreateRow(VillageResourceOverview overview)
    {
        var wood = Find(overview, "Wood");
        var clay = Find(overview, "Clay");
        var iron = Find(overview, "Iron");
        var crop = Find(overview, "Crop");

        return new VillageComparisonRow(
            overview.VillageName,
            FormatResource(wood, FindChange(overview, "Wood")),
            FormatResource(clay, FindChange(overview, "Clay")),
            FormatResource(iron, FindChange(overview, "Iron")),
            FormatResource(crop, FindChange(overview, "Crop")),
            $"W {FormatUtilization(wood)} · C {FormatUtilization(clay)} · I {FormatUtilization(iron)}",
            FormatUtilization(crop),
            overview.CapturedAt.ToLocalTime().ToString("g"));
    }

    private static ResourceGauge? Find(VillageResourceOverview overview, string name) =>
        overview.Resources.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));

    private static ResourceChange? FindChange(VillageResourceOverview overview, string name) =>
        overview.Changes.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));

    private static string FormatResource(ResourceGauge? gauge, ResourceChange? change)
    {
        if (gauge?.Stock is not int stock) return "Not detected";
        var delta = change is null ? "Δ —" : $"Δ {change.Delta:+#,0;-#,0;0}";
        return $"{stock:N0} ({delta})";
    }

    private static string FormatUtilization(ResourceGauge? gauge)
    {
        if (gauge is null || !gauge.HasCapacity) return "N/A";
        return gauge.IsOverCapacity
            ? $"{gauge.UtilizationPercent:0.0}% (over)"
            : $"{gauge.UtilizationPercent:0.0}%";
    }
}
