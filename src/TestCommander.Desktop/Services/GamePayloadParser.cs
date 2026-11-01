using System.Globalization;
using System.Text.Json;
using TestCommander.Models;

namespace TestCommander.Services;

/// <summary>
/// Conservative parser for explicitly named resource containers. It intentionally avoids
/// recursively searching arbitrary game payloads, where "wood" or "crop" can mean costs,
/// rewards, or unrelated values rather than the village's current stock.
/// </summary>
public static class GamePayloadParser
{
    private static readonly string[] ResourceContainerNames = ["resources", "resource"];

    public static VillageSnapshot? TryCreateSnapshot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        var resourceContainer = FindDirectObject(root, ResourceContainerNames);
        var values = resourceContainer ?? root;
        var wood = ReadInt(values, "wood", "lumber");
        var clay = ReadInt(values, "clay");
        var iron = ReadInt(values, "iron");
        var crop = ReadInt(values, "crop", "food");

        if (wood is null && clay is null && iron is null && crop is null)
            return null;

        var capacities = FindDirectObject(root, "capacities", "storage") ?? root;
        // A stable village identity is required before storing history. Without it,
        // snapshots from different villages could be merged under one fallback label.
        var villageName = ReadString(root, "villageName", "village_name", "name");
        if (villageName is null) return null;

        return new VillageSnapshot(
            villageName,
            wood,
            clay,
            iron,
            crop,
            ReadInt(capacities, "warehouseCapacity", "warehouse_capacity", "warehouse"),
            ReadInt(capacities, "granaryCapacity", "granary_capacity", "granary"),
            DateTimeOffset.UtcNow);
    }

    private static JsonElement? FindDirectObject(JsonElement parent, params string[] names)
    {
        foreach (var property in parent.EnumerateObject())
            if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Object)
                return property.Value;
        return null;
    }

    private static int? ReadInt(JsonElement parent, params string[] names)
    {
        foreach (var property in parent.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) continue;
            if (property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt32(out var number) && number >= 0)
                return number;
            if (property.Value.ValueKind == JsonValueKind.String &&
                int.TryParse(property.Value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out number) &&
                number >= 0)
                return number;
        }
        return null;
    }

    private static string? ReadString(JsonElement parent, params string[] names)
    {
        foreach (var property in parent.EnumerateObject())
            if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.Value.GetString()))
                return property.Value.GetString()!.Trim();
        return null;
    }
}