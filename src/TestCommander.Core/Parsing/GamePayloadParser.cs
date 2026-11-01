using System.Globalization;
using System.Text.Json;
using TestCommander.Core.Models;

namespace TestCommander.Core.Parsing;

/// <summary>
/// Conservative, schema-tolerant parser for sanitized game JSON.
/// It only interprets explicitly named fields and never performs game actions.
/// </summary>
public static class GamePayloadParser
{
    private static readonly string[] ResourceArrayNames = ["resources", "resourcedata", "resourcestatus"];
    private static readonly string[] QueueArrayNames = ["buildingqueue", "constructionqueue", "trainingqueue", "troopqueue"];
    private static readonly string[] MovementArrayNames = ["incomingattacks", "troopmovements", "movements", "attacks"];

    public static ParsedGameData Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
        var root = document.RootElement;
        var villageName = FindString(root, "villagename", "village_name", "name") ?? "Current village";
        var resources = FindResourceSnapshot(root, villageName);
        var queues = FindNamedArrayItems(root, QueueArrayNames)
            .Select(ParseQueue).Where(x => x is not null).Cast<QueueObservation>().Take(100).ToArray();
        var movements = FindNamedArrayItems(root, MovementArrayNames)
            .Select(ParseMovement).Where(x => x is not null).Cast<TroopMovementObservation>().Take(100).ToArray();

        return new ParsedGameData(resources, queues, movements);
    }

    private static ResourceSnapshot? FindResourceSnapshot(JsonElement root, string villageName)
    {
        JsonElement? best = null;
        var bestCount = 0;
        VisitObjects(root, element =>
        {
            var count = CountResourceFields(element);
            if (count > bestCount)
            {
                best = element;
                bestCount = count;
            }
        });

        if (best is null || bestCount < 2) return null;
        var resourceObject = best.Value;
        return new ResourceSnapshot(
            villageName,
            ReadInt(resourceObject, "wood", "lumber"),
            ReadInt(resourceObject, "clay"),
            ReadInt(resourceObject, "iron"),
            ReadInt(resourceObject, "crop", "food"),
            ReadInt(resourceObject, "warehousecapacity", "warehouse_capacity", "warehouse"),
            ReadInt(resourceObject, "granarycapacity", "granary_capacity", "granary"));
    }

    private static int CountResourceFields(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        var names = element.EnumerateObject().Select(p => Normalize(p.Name)).ToHashSet(StringComparer.Ordinal);
        var count = 0;
        if (names.Contains("wood") || names.Contains("lumber")) count++;
        if (names.Contains("clay")) count++;
        if (names.Contains("iron")) count++;
        if (names.Contains("crop") || names.Contains("food")) count++;
        return count;
    }

    private static IEnumerable<JsonElement> FindNamedArrayItems(JsonElement root, IReadOnlyCollection<string> acceptedNames)
    {
        var normalizedNames = acceptedNames.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var found = new List<JsonElement>();
        VisitObjects(root, element =>
        {
            foreach (var property in element.EnumerateObject())
            {
                if (normalizedNames.Contains(Normalize(property.Name)) && property.Value.ValueKind == JsonValueKind.Array)
                    found.AddRange(property.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object));
            }
        });
        return found;
    }

    private static QueueObservation? ParseQueue(JsonElement item)
    {
        var name = FindString(item, "buildingname", "unitname", "itemname", "name", "title");
        if (string.IsNullOrWhiteSpace(name)) return null;
        var queueType = FindString(item, "queuetype", "type") ?? "Game queue";
        return new QueueObservation(queueType, name, ReadInt(item, "level"),
            FindDate(item, "completesat", "completiontime", "finishtime", "endtime"));
    }

    private static TroopMovementObservation? ParseMovement(JsonElement item)
    {
        var origin = FindString(item, "origin", "from", "source", "fromvillage") ?? string.Empty;
        var destination = FindString(item, "destination", "to", "target", "tovillage") ?? string.Empty;
        var description = FindString(item, "description", "name", "movementname") ?? "Troop movement";
        if (origin.Length == 0 && destination.Length == 0 && description == "Troop movement") return null;
        var movementType = FindString(item, "movementtype", "type", "status") ?? "Movement";
        return new TroopMovementObservation(movementType, origin, destination,
            FindDate(item, "arrivesat", "arrivaltime", "arrival", "impacttime"), description);
    }

    private static string? FindString(JsonElement root, params string[] names)
    {
        var accepted = names.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        string? result = null;
        VisitObjects(root, element =>
        {
            if (result is not null) return;
            foreach (var property in element.EnumerateObject())
                if (accepted.Contains(Normalize(property.Name)) && property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) { result = value; return; }
                }
        });
        return result;
    }

    private static int? ReadInt(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var accepted = names.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!accepted.Contains(Normalize(property.Name))) continue;
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number)) return number;
            if (property.Value.ValueKind == JsonValueKind.String &&
                int.TryParse(property.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
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
                DateTimeOffset.TryParse(property.Value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                return parsed;
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var unix))
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(unix); }
                catch (ArgumentOutOfRangeException) { return null; }
            }
        }
        return null;
    }

    private static void VisitObjects(JsonElement element, Action<JsonElement> visitor, int depth = 0)
    {
        if (depth > 32) return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            visitor(element);
            foreach (var property in element.EnumerateObject()) VisitObjects(property.Value, visitor, depth + 1);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray().Take(250)) VisitObjects(item, visitor, depth + 1);
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
