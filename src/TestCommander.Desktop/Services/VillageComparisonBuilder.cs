using TestCommander.Models;

namespace TestCommander.Services;

/// <summary>Projects existing village overviews into a stable, sortable comparison view.</summary>
public static class VillageComparisonBuilder
{
    public static IReadOnlyList<VillageComparisonRow> Create(IEnumerable<VillageResourceOverview> overviews)
    {
        ArgumentNullException.ThrowIfNull(overviews);

        return overviews
            .Where(overview => overview is not null)
            .OrderBy(overview => overview.VillageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(overview => overview.VillageName, StringComparer.Ordinal)
            .Select(overview => new VillageComparisonRow(
                overview.VillageName,
                Format(overview, "Wood"),
                Format(overview, "Clay"),
                Format(overview, "Iron"),
                Format(overview, "Crop"),
                overview.CapturedAt))
            .ToArray();
    }

    private static string Format(VillageResourceOverview overview, string name)
    {
        var gauge = overview.Resources.FirstOrDefault(resource =>
            string.Equals(resource.Name, name, StringComparison.OrdinalIgnoreCase));
        if (gauge is null) return "Not detected";

        var stock = gauge.Stock?.ToString("N0") ?? "—";
        var capacity = gauge.Capacity is > 0 ? gauge.Capacity.Value.ToString("N0") : "cap —";
        var utilization = gauge.HasCapacity ? $"{gauge.UtilizationPercent:0}%" : "—";
        var change = overview.Changes.FirstOrDefault(item =>
            string.Equals(item.ResourceName, name, StringComparison.OrdinalIgnoreCase));
        var delta = change is null ? "Δ —" : $"Δ {change.Delta:+#,0;-#,0;0}";
        return $"{stock} / {capacity} · {utilization} · {delta}";
    }
}
