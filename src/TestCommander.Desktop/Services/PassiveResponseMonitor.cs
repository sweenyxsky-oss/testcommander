using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using TestCommander.Models;

namespace TestCommander.Services;

/// <summary>
/// Observes JSON responses from the currently displayed origin. It never sends requests,
/// replays actions, reads cookies, or persists response bodies or authentication material.
/// </summary>
public sealed class PassiveResponseMonitor(DatabaseService database)
{
    private const int MaxBodyBytes = 1_000_000;
    private Uri? _allowedOrigin;
    private int _isAttached;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<VillageSnapshot>? SnapshotCaptured;

    public void SetAllowedOrigin(Uri? origin)
    {
        _allowedOrigin = origin is null ? null : new Uri(origin.GetLeftPart(UriPartial.Authority));
    }

    public void Attach(CoreWebView2 core)
    {
        if (Interlocked.Exchange(ref _isAttached, 1) == 1) return;
        core.WebResourceResponseReceived += OnResponseReceived;
        StatusChanged?.Invoke(this, "Passive monitoring enabled for the current site.");
    }

    public void Detach(CoreWebView2 core)
    {
        if (Interlocked.Exchange(ref _isAttached, 0) == 0) return;
        core.WebResourceResponseReceived -= OnResponseReceived;
    }

    private void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        _ = ProcessResponseAsync(args);
    }

    private async Task ProcessResponseAsync(CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        try
        {
            if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var requestUri)) return;
            var allowedOrigin = _allowedOrigin;
            if (allowedOrigin is null ||
                !string.Equals(requestUri.GetLeftPart(UriPartial.Authority), allowedOrigin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
                return;

            var response = args.Response;
            var contentType = response.Headers.Contains("Content-Type")
                ? response.Headers.GetHeader("Content-Type")
                : string.Empty;
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return;
            if (response.StatusCode is < 200 or >= 300) return;

            await using var stream = await response.GetContentAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes) return;
                buffer.Write(chunk, 0, read);
            }

            using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var summary = JsonSummary.Create(document.RootElement);
            await database.SaveResponseAsync(new(requestUri.AbsolutePath, contentType, response.StatusCode,
                checked((int)buffer.Length), DateTimeOffset.UtcNow, summary));

            var snapshot = GamePayloadParser.TryCreateSnapshot(document.RootElement);
            if (snapshot is not null)
            {
                await database.SaveSnapshotAsync(snapshot);
                SnapshotCaptured?.Invoke(this, snapshot);
            }
            StatusChanged?.Invoke(this, $"Observed JSON response: {requestUri.AbsolutePath} ({buffer.Length:N0} bytes).");
        }
        catch (JsonException)
        {
            StatusChanged?.Invoke(this, "A response was skipped because it was not valid JSON.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Monitor skipped a response: {ex.GetType().Name}.");
        }
    }
}

internal static class JsonSummary
{
    public static string Create(JsonElement root)
    {
        var keys = new List<string>();
        CollectKeys(root, keys, 0);
        return keys.Count == 0 ? "JSON payload observed" : "Fields: " + string.Join(", ", keys.Distinct().Take(12));
    }

    private static void CollectKeys(JsonElement element, List<string> keys, int depth)
    {
        if (depth > 5) return;
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (keys.Count < 40) keys.Add(property.Name);
                CollectKeys(property.Value, keys, depth + 1);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray().Take(20))
                CollectKeys(item, keys, depth + 1);
    }
}

internal static class GamePayloadParser
{
    public static VillageSnapshot? TryCreateSnapshot(JsonElement root)
    {
        var values = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        Flatten(root, values, 0);
        var wood = Find(values, "wood", "lumber");
        var clay = Find(values, "clay");
        var iron = Find(values, "iron");
        var crop = Find(values, "crop", "food");
        if (wood is null && clay is null && iron is null && crop is null) return null;

        var village = FindString(root, "villageName", "village_name") ?? "Current village";
        return new(village, wood, clay, iron, crop,
            Find(values, "warehouseCapacity", "warehouse_capacity"),
            Find(values, "granaryCapacity", "granary_capacity"), DateTimeOffset.UtcNow);
    }

    private static void Flatten(JsonElement element, Dictionary<string, int?> values, int depth)
    {
        if (depth > 8) return;
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number))
                    values[property.Name] = number;
                else if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out number))
                    values[property.Name] = number;
                else
                    Flatten(property.Value, values, depth + 1);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray().Take(100))
                Flatten(item, values, depth + 1);
    }

    private static int? Find(Dictionary<string, int?> values, params string[] aliases)
    {
        foreach (var alias in aliases)
            foreach (var pair in values)
                if (string.Equals(pair.Key, alias, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
        return null;
    }

    private static string? FindString(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
                var nested = FindString(property.Value, names);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray().Take(50))
            {
                var nested = FindString(item, names);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        return null;
    }
}