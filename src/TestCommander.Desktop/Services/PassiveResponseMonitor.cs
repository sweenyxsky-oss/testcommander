using System.IO;
using System.Text;
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
    private const int MaxConcurrentResponses = 3;
    private readonly SemaphoreSlim _processingSlots = new(MaxConcurrentResponses, MaxConcurrentResponses);
    private Uri? _allowedOrigin;
    private int _isAttached;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler? DataCaptured;

    public void SetAllowedOrigin(Uri? origin)
    {
        var safeOrigin = origin is null ? null : new Uri(origin.GetLeftPart(UriPartial.Authority));
        Volatile.Write(ref _allowedOrigin, safeOrigin);
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
        SetAllowedOrigin(null);
    }

    private void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        if (!_processingSlots.Wait(0)) return;
        _ = ProcessResponseAsync(args);
    }

    private async Task ProcessResponseAsync(CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        try
        {
            if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var requestUri)) return;
            var allowedOrigin = Volatile.Read(ref _allowedOrigin);
            if (allowedOrigin is null || !SameOrigin(requestUri, allowedOrigin)) return;

            var response = args.Response;
            var contentType = response.Headers.Contains("Content-Type")
                ? response.Headers.GetHeader("Content-Type") : string.Empty;
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                response.StatusCode is < 200 or >= 300) return;

            await using var stream = await response.GetContentAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes) return;
                await buffer.WriteAsync(chunk.AsMemory(0, read)).ConfigureAwait(false);
            }

            var json = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
            var parsed = GamePayloadParser.Parse(json);

            // The browser may have navigated while the response body was being read.
            if (Volatile.Read(ref _isAttached) == 0 ||
                !SameOrigin(requestUri, Volatile.Read(ref _allowedOrigin))) return;

            var capturedAt = DateTimeOffset.UtcNow;
            var safePath = SanitizePath(requestUri.AbsolutePath);
            var summary = $"Resources: {(parsed.Resources is null ? "no" : "yes")}; queues: {parsed.Queues.Count}; troop movements: {parsed.TroopMovements.Count}";
            await database.SaveResponseAsync(new(safePath, contentType, response.StatusCode,
                checked((int)buffer.Length), capturedAt, summary)).ConfigureAwait(false);

            if (parsed.Resources is not null)
                await database.SaveSnapshotAsync(VillageSnapshot.From(parsed.Resources, capturedAt)).ConfigureAwait(false);

            await database.SaveObservationsAsync(parsed.Queues, parsed.TroopMovements, capturedAt).ConfigureAwait(false);
            DataCaptured?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, $"Observed JSON response: {safePath} ({buffer.Length:N0} bytes).");
        }
        catch (System.Text.Json.JsonException)
        {
            StatusChanged?.Invoke(this, "A response was skipped because it was not valid JSON.");
        }
        catch (ObjectDisposedException)
        {
            // A WebView2 response stream can become unavailable during navigation or shutdown.
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Monitor skipped a response: {ex.GetType().Name}.");
        }
        finally
        {
            _processingSlots.Release();
        }
    }

    private static string SanitizePath(string path)
    {
        var withoutGuid = System.Text.RegularExpressions.Regex.Replace(path,
            @"(?i)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "{id}");
        return System.Text.RegularExpressions.Regex.Replace(withoutGuid, @"(?<![A-Za-z])\d{5,}", "{id}");
    }

    private static bool SameOrigin(Uri candidate, Uri? allowed) =>
        allowed is not null &&
        string.Equals(candidate.Scheme, allowed.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.IdnHost, allowed.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        candidate.Port == allowed.Port;
}
