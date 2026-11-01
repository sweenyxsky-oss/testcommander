using System.IO;
using Microsoft.Web.WebView2.Core;
using TestCommander.Core.Parsing;
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

    public void SetAllowedOrigin(Uri? origin) =>
        _allowedOrigin = origin is null ? null : new Uri(origin.GetLeftPart(UriPartial.Authority));

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

    private void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args) =>
        _ = ProcessResponseAsync(args);

    private async Task ProcessResponseAsync(CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        try
        {
            if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var requestUri)) return;
            var allowedOrigin = Volatile.Read(ref _allowedOrigin);
            if (allowedOrigin is null ||
                !string.Equals(requestUri.GetLeftPart(UriPartial.Authority), allowedOrigin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
                return;

            var response = args.Response;
            var contentType = response.Headers.Contains("Content-Type") ? response.Headers.GetHeader("Content-Type") : string.Empty;
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || response.StatusCode is < 200 or >= 300) return;

            await using var stream = await response.GetContentAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes) return;
                buffer.Write(chunk, 0, read);
            }

            var json = System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
            var parsed = GamePayloadParser.Parse(json);
            await database.SaveResponseAsync(new(requestUri.AbsolutePath, contentType, response.StatusCode,
                checked((int)buffer.Length), DateTimeOffset.UtcNow,
                $"Resources: {(parsed.Resources is null ? "no" : "yes")}; queues: {parsed.Queues.Count}; troop movements: {parsed.TroopMovements.Count}"));

            if (parsed.Resources is not null)
            {
                var snapshot = VillageSnapshot.From(parsed.Resources, DateTimeOffset.UtcNow);
                await database.SaveSnapshotAsync(snapshot);
                SnapshotCaptured?.Invoke(this, snapshot);
            }
            await database.SaveObservationsAsync(parsed.Queues, parsed.TroopMovements, DateTimeOffset.UtcNow);
            StatusChanged?.Invoke(this, $"Observed JSON response: {requestUri.AbsolutePath} ({buffer.Length:N0} bytes).");
        }
        catch (System.Text.Json.JsonException)
        {
            StatusChanged?.Invoke(this, "A response was skipped because it was not valid JSON.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Monitor skipped a response: {ex.GetType().Name}.");
        }
    }
}
