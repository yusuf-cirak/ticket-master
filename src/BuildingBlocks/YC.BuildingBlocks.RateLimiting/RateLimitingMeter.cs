using System.Diagnostics.Metrics;
using System.Threading;
using YC.BuildingBlocks.Domain.Observability;

namespace YC.BuildingBlocks.RateLimiting;

/// <summary>
/// OpenTelemetry meter for rate limiting. Registered in the observability pipeline via
/// <c>AddMeter(MeterName)</c> so the counters/histogram ship over OTLP to SigNoz.
/// </summary>
internal static class RateLimitingMeter
{
    public const string MeterName = TelemetryConstants.RateLimitingMeterName;

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

    private static int _loadGaugesRegistered;

    /// <summary>
    /// Registers the <c>adaptive.load.*</c> observable gauges (composite score + its components), so operators can see
    /// WHY the admission limit moved. Idempotent — registered once even if multiple hosts share this static meter.
    /// </summary>
    public static void TrackLoad(LoadSignalState state)
    {
        if (Interlocked.Exchange(ref _loadGaugesRegistered, 1) != 0)
            return;

        Meter.CreateObservableGauge("adaptive.load.score", () => state.Current.Score, description: "Composite load score [0..1] driving AIMD.");
        Meter.CreateObservableGauge("adaptive.load.cpu", () => state.Current.Cpu, description: "CPU component of the load score [0..1].");
        Meter.CreateObservableGauge("adaptive.load.threadpool", () => state.Current.ThreadPool, description: "Thread-pool backlog component [0..1].");
        Meter.CreateObservableGauge("adaptive.load.latency", () => state.Current.Latency, description: "Latency component [0..1].");
        Meter.CreateObservableGauge("adaptive.load.errors", () => state.Current.Error, description: "5xx error-rate component [0..1].");
    }
}
