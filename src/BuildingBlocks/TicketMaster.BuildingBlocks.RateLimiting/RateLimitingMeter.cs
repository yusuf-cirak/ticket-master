using System.Diagnostics.Metrics;
using System.Threading;

namespace TicketMaster.BuildingBlocks.RateLimiting;

/// <summary>
/// OpenTelemetry meter for rate limiting. Registered in the observability pipeline via
/// <c>AddMeter(MeterName)</c> so the counters/histogram ship over OTLP to SigNoz.
/// </summary>
internal static class RateLimitingMeter
{
    public const string MeterName = "TicketMaster.RateLimiting";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Rejected =
        Meter.CreateCounter<long>("ratelimit.rejected", description: "Requests rejected (429) by the rate limiter.");

    public static readonly Counter<long> Banned =
        Meter.CreateCounter<long>("burst.banned", description: "Burst bans armed after a caller exceeded the burst limit.");

    public static readonly Counter<long> Fallback =
        Meter.CreateCounter<long>("ratelimit.fallback", description: "Times the limiter degraded to the in-memory fallback (Redis unavailable).");

    // Layer 2 — adaptive admission control (AIMD).
    public static readonly Counter<long> AdaptiveRejected =
        Meter.CreateCounter<long>("adaptive.rejected", description: "Requests shed (503) by the adaptive admission gate under load.");

    private static int _limitGaugeRegistered;

    /// <summary>
    /// Registers the <c>adaptive.limit</c> observable gauge over the given admission-limit holder. Idempotent — the
    /// first caller wins, so multiple hosts sharing this static meter (e.g. in tests) register the gauge only once.
    /// </summary>
    public static void TrackAdmissionLimit(AdmissionLimit limit)
    {
        if (Interlocked.Exchange(ref _limitGaugeRegistered, 1) == 0)
            Meter.CreateObservableGauge("adaptive.limit", () => limit.Current,
                description: "Current global admission limit (max concurrent in-flight admitted requests).");
    }
}
