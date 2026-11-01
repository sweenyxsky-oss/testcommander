using TestCommander.Models;

namespace TestCommander.Services;

/// <summary>
/// Calculates display-only storage utilization and changes between observed snapshots.
/// It does not estimate production or infer missing capacity values.
/// </summary>
public static class VillageResourceCalculator
{
    public static VillageResourceOverview Create(VillageSnapshot snapshot, VillageSnapshot? previous = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var changes = previous is null
            ? Array.Empty<ResourceChange>()
            : CreateChanges(snapshot, previous);

        return new VillageResourceOverview(
            snapshot.VillageName,
            snapshot.CapturedAt,
            [
                new ResourceGauge("Wood", snapshot.Wood, snapshot.WarehouseCapacity),
                new ResourceGauge("Clay", snapshot.Clay, snapshot.WarehouseCapacity),
                new ResourceGauge("Iron", snapshot.Iron, snapshot.WarehouseCapacity),
                new ResourceGauge("Crop", snapshot.Crop, snapshot.GranaryCapacity)
            ],
            changes);
    }

    private static IReadOnlyList<ResourceChange> CreateChanges(VillageSnapshot current, VillageSnapshot previous)
    {
        if (!string.Equals(current.VillageName, previous.VillageName, StringComparison.Ordinal))
            return Array.Empty<ResourceChange>();

        var changes = new List<ResourceChange>(4);
        AddChange(changes, "Wood", previous.Wood, current.Wood);
        AddChange(changes, "Clay", previous.Clay, current.Clay);
        AddChange(changes, "Iron", previous.Iron, current.Iron);
        AddChange(changes, "Crop", previous.Crop, current.Crop);
        return changes;
    }

    private static void AddChange(List<ResourceChange> changes, string name, int? previous, int? current)
    {
        if (previous.HasValue && current.HasValue)
            changes.Add(new ResourceChange(name, current.Value - previous.Value));
    }
}