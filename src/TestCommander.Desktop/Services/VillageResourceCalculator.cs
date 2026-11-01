using TestCommander.Models;

namespace TestCommander.Services;

/// <summary>
/// Calculates display-only warehouse/granary utilization from a captured snapshot.
/// It does not estimate production or infer missing capacity values.
/// </summary>
public static class VillageResourceCalculator
{
    public static VillageResourceOverview Create(VillageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new VillageResourceOverview(
            snapshot.VillageName,
            snapshot.CapturedAt,
            [
                new ResourceGauge("Wood", snapshot.Wood, snapshot.WarehouseCapacity),
                new ResourceGauge("Clay", snapshot.Clay, snapshot.WarehouseCapacity),
                new ResourceGauge("Iron", snapshot.Iron, snapshot.WarehouseCapacity),
                new ResourceGauge("Crop", snapshot.Crop, snapshot.GranaryCapacity)
            ]);
    }
}