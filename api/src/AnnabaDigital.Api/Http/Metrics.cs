using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Persistence;

namespace AnnabaDigital.Api.Http;

/// <summary>
/// Request counters and latency histograms kept in memory and exposed in the Prometheus text format, with no extra dependency.
/// Labels are the HTTP method, the route template (never the raw path, so the number of series stays small and no id ends up in a label) and the status class.
/// </summary>
public sealed class HttpMetrics
{
    public static readonly double[] Buckets = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];
    private sealed class Series { public long Count; public double Sum; public readonly long[] Bucket = new long[Buckets.Length]; }
    private readonly ConcurrentDictionary<(string Method, string Route, string Status), Series> _series = new();
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public void Observe(string method, string route, int status, double seconds)
    {
        var s = _series.GetOrAdd((method, route, $"{status / 100}xx"), _ => new Series());
        lock (s) { s.Count++; s.Sum += seconds; for (var i = 0; i < Buckets.Length; i++) if (seconds <= Buckets[i]) s.Bucket[i]++; }
    }

    public void Write(StringBuilder sb)
    {
        static string L(string v) => v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
        var inv = CultureInfo.InvariantCulture;
        sb.Append("# HELP http_requests_total Requests handled, by method, route template and status class.\n# TYPE http_requests_total counter\n");
        foreach (var (k, s) in _series.OrderBy(x => x.Key.Route).ThenBy(x => x.Key.Method).ThenBy(x => x.Key.Status))
            lock (s) sb.Append(inv, $"http_requests_total{{method=\"{L(k.Method)}\",route=\"{L(k.Route)}\",status=\"{k.Status}\"}} {s.Count}\n");
        sb.Append("# HELP http_request_duration_seconds Time to handle a request.\n# TYPE http_request_duration_seconds histogram\n");
        foreach (var (k, s) in _series.OrderBy(x => x.Key.Route).ThenBy(x => x.Key.Method).ThenBy(x => x.Key.Status))
            lock (s)
            {
                var labels = $"method=\"{L(k.Method)}\",route=\"{L(k.Route)}\",status=\"{k.Status}\"";
                for (var i = 0; i < Buckets.Length; i++) sb.Append(inv, $"http_request_duration_seconds_bucket{{{labels},le=\"{Buckets[i].ToString(inv)}\"}} {s.Bucket[i]}\n");
                sb.Append(inv, $"http_request_duration_seconds_bucket{{{labels},le=\"+Inf\"}} {s.Count}\nhttp_request_duration_seconds_sum{{{labels}}} {s.Sum.ToString("0.######", inv)}\nhttp_request_duration_seconds_count{{{labels}}} {s.Count}\n");
            }
    }
}

public sealed class MetricsMiddleware(RequestDelegate next, HttpMetrics metrics)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var sw = Stopwatch.GetTimestamp();
        try { await next(ctx); }
        finally
        {
            var route = (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            if (!route.StartsWith("/health", StringComparison.Ordinal)) metrics.Observe(ctx.Request.Method, route, ctx.Response.StatusCode, Stopwatch.GetElapsedTime(sw).TotalSeconds);
        }
    }
}

public static class MetricsEndpoint
{
    /// <summary>
    /// GET /health/metrics for the monitoring system. It lives under /health so it can be scraped inside the cluster without the UI proxy key, and answers only to
    /// "Authorization: Bearer &lt;Api:MetricsToken&gt;". Without a configured token (at least 24 characters) the route does not exist (404).
    /// Besides the request metrics it reports queue depths across all organisations: e-mails and webhooks waiting or failed, and requests waiting for approval.
    /// </summary>
    public static void MapMetrics(this WebApplication app)
    {
        app.MapGet("/health/metrics", async (HttpContext ctx, HttpMetrics metrics, IOptions<ApiOptions> options, ITokenService tokens, AnnabaDigitalDbContext db, IClock clock, CancellationToken ct) =>
        {
            var token = options.Value.MetricsToken; var sent = ctx.Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(token) || token.Length < 24 || !sent.StartsWith("Bearer ", StringComparison.Ordinal) || !tokens.ConstantTimeEquals(sent[7..], token)) return Results.NotFound();
            var inv = CultureInfo.InvariantCulture; var sb = new StringBuilder(); var now = clock.UtcNow;
            void Gauge(string name, string help, double value) => sb.Append(inv, $"# HELP {name} {help}\n# TYPE {name} gauge\n{name} {value.ToString("0.###", inv)}\n");
            var proc = Process.GetCurrentProcess();
            Gauge("process_uptime_seconds", "Seconds since this instance started.", (DateTimeOffset.UtcNow - metrics.StartedAt).TotalSeconds);
            Gauge("process_resident_memory_bytes", "Resident memory of this instance.", proc.WorkingSet64);
            Gauge("dotnet_gc_heap_bytes", "Managed heap size.", GC.GetTotalMemory(false));
            try
            {
                var mail = db.OutboxEmails.IgnoreQueryFilters().AsNoTracking();
                Gauge("outbox_emails_pending", "E-mails waiting to be sent.", await mail.CountAsync(m => m.SentAt == null && m.FailedAt == null, ct));
                Gauge("outbox_emails_failed", "E-mails that were given up on.", await mail.CountAsync(m => m.FailedAt != null, ct));
                var oldest = await mail.Where(m => m.SentAt == null && m.FailedAt == null).OrderBy(m => m.CreatedAt).Select(m => (DateTimeOffset?)m.CreatedAt).FirstOrDefaultAsync(ct);
                Gauge("outbox_emails_oldest_pending_seconds", "Age of the oldest e-mail still waiting.", oldest is { } o ? Math.Max(0, (now - o).TotalSeconds) : 0);
                var hooks = db.WebhookDeliveries.IgnoreQueryFilters().AsNoTracking();
                Gauge("webhook_deliveries_pending", "Webhook deliveries waiting or being retried.", await hooks.CountAsync(d => d.Status == DeliveryStatus.Pending, ct));
                Gauge("webhook_deliveries_dead", "Webhook deliveries given up on.", await hooks.CountAsync(d => d.Status == DeliveryStatus.Dead, ct));
                var cutoff = now.AddDays(-3);
                Gauge("hr_leave_requests_pending_over_3d", "Leave requests undecided for more than three days.", await db.LeaveRequests.IgnoreQueryFilters().CountAsync(l => l.Status == LeaveStatus.Pending && l.CreatedAt < cutoff, ct));
                Gauge("hr_pay_runs_awaiting_approval", "Calculated pay runs nobody has approved yet.", await db.PayRuns.IgnoreQueryFilters().CountAsync(p => p.Status == PayRunStatus.Calculated, ct));
                Gauge("database_up", "1 when the database answered this scrape.", 1);
            }
            catch (Exception e) when (e is not OperationCanceledException) { Gauge("database_up", "1 when the database answered this scrape.", 0); }
            metrics.Write(sb);
            return Results.Text(sb.ToString(), "text/plain; version=0.0.4; charset=utf-8");
        }).AllowAnonymous().ExcludeFromDescription();
    }
}
