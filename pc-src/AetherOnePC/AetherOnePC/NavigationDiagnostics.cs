using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace AetherOnePC;

/// <summary>
/// Bounded, thread-safe real navigation samples. No synthetic speed scores.
/// URLs are reduced to host names so browsing paths and query strings are not retained.
/// </summary>
internal sealed class NavigationDiagnostics
{
    private const int Capacity = 256;
    private readonly object gate = new();
    private readonly Queue<NavigationSample> samples = new();
    private readonly Dictionary<string, Stopwatch> inFlight = new(StringComparer.OrdinalIgnoreCase);
    internal sealed record NavigationSample(string Host, bool Success, long ElapsedMilliseconds, DateTimeOffset FinishedAt);
    internal void Start(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        lock (gate)
        {
            // Retain one timer per origin, avoiding full-URL retention.
            inFlight[uri.IdnHost] = Stopwatch.StartNew();
            if (inFlight.Count > Capacity) inFlight.Clear();
        }
    }
    internal void Complete(string url, bool success, long measuredMilliseconds)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        lock (gate)
        {
            long elapsed = measuredMilliseconds;
            if (inFlight.Remove(uri.IdnHost, out var timer))
            {
                timer.Stop();
                elapsed = timer.ElapsedMilliseconds;
            }
            samples.Enqueue(new NavigationSample(uri.IdnHost, success, Math.Max(0, elapsed), DateTimeOffset.UtcNow));
            while (samples.Count > Capacity) samples.Dequeue();
        }
    }
    internal string ExportJson()
    {
        lock (gate)
        {
            var successful = samples.Where(s => s.Success).Select(s => s.ElapsedMilliseconds).Order().ToArray();
            long percentile(double fraction) => successful.Length == 0 ? 0 :
                successful[Math.Clamp((int)Math.Ceiling(fraction * successful.Length) - 1, 0, successful.Length - 1)];
            return JsonSerializer.Serialize(new
            {
                sampleCount = samples.Count,
                successCount = successful.Length,
                p50NavigationMs = percentile(.5),
                p95NavigationMs = percentile(.95),
                // This is navigation completion, not LCP, full resource load, or video readiness.
                metric = "WebView2 NavigationCompleted elapsed milliseconds",
                samples = samples.ToArray()
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
