using System.Globalization;
using System.Text.Json;
using TestCommander.Core.Models;

namespace TestCommander.Core.Parsing;

/// <summary>
/// Conservative, read-only parser for observed JSON. Field aliases are intentionally
/// generic until sanitized payloads from the target game establish its real schema.
/// </summary>
public static class GamePayloadParser
{
    private static readonly HashSet<string> ResourceContainerNames =
        ["resources", "resourcedata", "resourcestatus"];
    private static readonly HashSet<string> QueueArrayNames =
        ["buildingqueue", "constructionqueue", "trainingqueue", "troopqueue",
         "buildingqueueitems", "constructionqueueitems", "trainingqueueitems"];
    private static readonly HashSet<string> MovementArrayNames =
        ["incomingattacks", "outgoingattacks", "incomingmovements", "outgoingmovements",
         "ownmovements", "troopmovements", "movements", "attackmovements", "supportmovements"];

    public static ParsedGameData Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
        var root = document.RootElement;
        var villageName = FindDirectString(root, "villagename", "village_name") ?? "Current village";
        return new ParsedGameData(
            FindResourceSnapshot(root, villageName),
            FindNamedArrayItems(root, QueueArrayNames)
                .Select(x => ParseQueue(x.Item, x.ArrayName))
                .Where(x => x is not null).Cast<QueueObservation>().Take(100).ToArray(),
            FindNamedArrayItems(root, MovementArrayNames)
                .Select(x => ParseMovement(x.Item, x.ArrayName))
                .Where(x => x is not null).Cast<TroopMovementObservation>().Take(100).ToArray());
    }

    private static ResourceSnapshot? FindResourceSnapshot(JsonElement root, string villageName)
    {
        JsonElement? best = null;
        var bestCount = 0;
        VisitObjects(root, (element, parentName) =>
        {
            var count = CountResourceFields(element);
            var namedContainer = parentName is not null && ResourceContainerNames.Contains(Normalize(parentName));
            var rootResourceObject = parentName is null && count >= 3;
            if ((namedContainer || rootResourceObject) && count > bestCount)
            {
                best = element;
                bestCount = count;
            }
        });

        if (best is null || bestCount < 2) return null;
        var resourceObject = best.Value;
        return new ResourceSnapshot(villageName,
            ReadNonNegativeInt(resourceObject, "wood", "lumber"),
            ReadNonNegativeInt(resourceObject, "clay"),
            ReadNonNegativeInt(resourceObject, "iron"),
            ReadNonNegativeInt(resourceObject, "crop", "food"),
            ReadNonNegativeInt(resourceObject, "warehousecapacity", "warehouse_capacity", "warehouse"),
            ReadNonNegativeInt(resourceObject, "granarycapacity", "granary_capacity", "granary"));
    }

    private static int CountResourceFields(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        var names = element.EnumerateObject().Select(p => Normalize(p.Name)).ToHashSet(StringComparer.Ordinal);
        return (names.Contains("wood") || names.Contains("lumber") ? 1 : 0)
            + (names.Contains("clay") ? 1 : 0)
            + (names.Contains("iron") ? 1 : 0)
            + (names.Contains("crop") || names.Contains("food") ? 1 : 0);
    }

    private static IEnumerable<(string ArrayName, JsonElement Item)> FindNamedArrayItems(
        JsonElement root, IReadOnlySet<string> acceptedNames)
    {
        var found = new List<(string ArrayName, JsonElement Item)>();
        VisitObjects(root, (element, _) =>
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!acceptedNames.Contains(Normalize(property.Name)) ||
                    property.Value.ValueKind != JsonValueKind.Array) continue;

                found.AddRange(property.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object)
                    .Take(100)
                    .Select(item => (property.Name, item)));
            }
        });
        return found;
    }

    private static QueueObservation? ParseQueue(JsonElement item, string arrayName)
    {
        var name = FindString(item, "buildingname", "unitname", "itemname", "name", "title");
        if (string.IsNullOrWhiteSpace(name)) return null;

        var queueLabel = HumanizeArrayName(arrayName);
        if (queueLabel == "Observed")
            queueLabel = FindString(item, "queuetype", "type") ?? queueLabel;

        return new QueueObservation(queueLabel, name,
            ReadNonNegativeInt(item, "level"),
            FindDate(item, "completesat", "completiontime", "finishtime", "endtime"));
    }

    private static TroopMovementObservation? ParseMovement(JsonElement item, string arrayName)
    {
        var origin = FindString(item, "origin", "from", "source", "fromvillage") ?? string.Empty;
        var destination = FindString(item, "destination", "to", "target", "tovillage") ?? string.Empty;
        var description = FindString(item, "description", "name", "movementname") ?? "Troop movement";
        if (origin.Length == 0 && destination.Length == 0 && description == "Troop movement") return null;

        var arrayLabel = HumanizeArrayName(arrayName);
        var directionIsExplicit = arrayLabel.Contains("Incoming", StringComparison.OrdinalIgnoreCase)
            || arrayLabel.Contains("Outgoing", StringComparison.OrdinalIgnoreCase)
            || arrayLabel.Contains("Own", StringComparison.OrdinalIgnoreCase);
        var movementType = directionIsExplicit
            ? arrayLabel
            : FindString(item, "movementtype", "type", "status") ?? arrayLabel;

        return new TroopMovementObservation(movementType, origin, destination,
            FindDate(item, "arrivesat", "arrivaltime", "arrival", "impacttime"), description);
    }

    private static string HumanizeArrayName(string value)
    {
        var spaced = System.Text.RegularExpressions.Regex.Replace(value, "([a-z])([A-Z])", "$1 $2")
            .Replace('_', ' ').Trim();
        if (spaced.Length == 0) return "Observed";
        var words = spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static string? FindDirectString(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        var accepted = names.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (accepted.Contains(Normalize(property.Name)) &&
                property.Value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.Value.GetString()))
                return property.Value.GetString();
        return null;
    }

    private static string? FindString(JsonElement root, params string[] names)
    {
        var accepted = names.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        string? result = null;
        VisitObjects(root, (element, _) =>
        {
            if (result is not null) return;
            foreach (var property in element.EnumerateObject())
                if (accepted.Contains(Normalize(property.Name)) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    result = property.Value.GetString();
                    return;
                }
        });
        return result;
    }

    private static int? ReadNonNegativeInt(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var accepted = names.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!accepted.Contains(Normalize(property.Name))) continue;
            int? parsed = null;
            if (property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt32(out var numericValue))
                parsed = numericValue;
            else if (property.Value.ValueKind == JsonValueKind.String &&
                int.TryParse(property.Value.GetString(), NumberStyles.Integer | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out var stringValue))
                parsed = stringValue;

            if (parsed is >= 0) return parsed;
        }
        return null;
    }

    private static DateTimeOffset? FindDate(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var accepted = names.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!accepted.Contains(Normalize(property.Name))) continue;
            if (property.Value.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(property.Value.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var parsed))
                return parsed;

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var unix))
            {
                try
                {
                    return unix >= 100_000_000_000L || unix <= -100_000_000_000L
                        ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                        : DateTimeOffset.FromUnixTimeSeconds(unix);
                }
                catch (ArgumentOutOfRangeException) { return null; }
            }
        }
        return null;
    }

    private static void VisitObjects(JsonElement element, Action<JsonElement, string?> visitor,
        string? parentName = null, int depth = 0)
    {
        if (depth > 32) return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            visitor(element, parentName);
            foreach (var property in element.EnumerateObject())
                VisitObjects(property.Value, visitor, property.Name, depth + 1);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray().Take(250))
                VisitObjects(item, visitor, parentName, depth + 1);
        }
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
