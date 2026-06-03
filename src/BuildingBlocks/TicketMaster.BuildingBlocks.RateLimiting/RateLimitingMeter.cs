using System.Diagnostics.Metrics;

namespace TicketMaster.BuildingBlocks.RateLimiting;

/// <summary>
/// OpenTelemetry meter for rate limiting. Registered in the observability pipeline via
/// <c>AddMeter(MeterName)</c> so the counters/histogram ship over OTLP to SigNoz.
/// </summary>
internal static class RateLimitingMeter
{
    public const string MeterName = "TicketMaster.RateLimiting";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Allowed =
        Meter.CreateCounter<long>("ratelimit.allowed", description: "Requests admitted by the rate limiter.");

    public static readonly Counter<long> Rejected =
        Meter.CreateCounter<long>("ratelimit.rejected", description: "Requests rejected (429) by the rate limiter.");

    public static readonly Counter<long> Fallback =
        Meter.CreateCounter<long>("ratelimit.fallback", description: "Times the limiter degraded to the in-memory fallback (Redis unavailable).");
}
