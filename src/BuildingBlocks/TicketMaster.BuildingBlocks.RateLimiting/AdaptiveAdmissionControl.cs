using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TicketMaster.BuildingBlocks.RateLimiting;

/// <summary>
/// The single global admission limit (max concurrent in-flight admitted requests), adjusted by the AIMD controller
/// and read by the in-process gate. Volatile access — one writer (the controller), many readers (request threads).
/// </summary>
internal sealed class AdmissionLimit(int initial)
{
    private int _current = initial;

    public int Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value);
    }
}

/// <summary>The AIMD step, extracted pure so it's testable without the background timer.</summary>
internal static class Aimd
{
    /// <summary>
    /// Additive-increase when healthy (<c>score &lt; HealthyThreshold</c>), multiplicative-decrease (halve) when
    /// overloaded (<c>score &gt; OverloadThreshold</c>), hold in between. Result is always clamped to [Min, Max].
    /// </summary>
    public static int NextLimit(int current, double score, AdaptiveOptions o)
    {
        var next = score < o.HealthyThreshold ? current + o.Step
                 : score > o.OverloadThreshold ? current / 2
                 : current;

        return Math.Clamp(next, o.MinLimit, o.MaxLimit);
    }
}

/// <summary>In-process concurrency gate: admits a request iff in-flight count stays within the current admission limit.</summary>
internal interface IAdmissionGate
{
    /// <summary>Tries to take a slot. On success returns a lease that frees the slot on <see cref="IDisposable.Dispose"/>.</summary>
    bool TryEnter(out IDisposable lease);
}

internal sealed class InProcAdmissionGate(AdmissionLimit limit) : IAdmissionGate
{
    private static readonly IDisposable Rejected = new NoopLease();
    private int _inFlight;

    public bool TryEnter(out IDisposable lease)
    {
        if (Interlocked.Increment(ref _inFlight) <= limit.Current)
        {
            lease = new Lease(this);
            return true;
        }

        Interlocked.Decrement(ref _inFlight); // over the limit — give the slot back
        lease = Rejected;
        return false;
    }

    private void Exit() => Interlocked.Decrement(ref _inFlight);

    private sealed class Lease(InProcAdmissionGate gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) // decrement exactly once
                gate.Exit();
        }
    }

    private sealed class NoopLease : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>
/// Background AIMD controller: every <see cref="AdaptiveOptions.ControlInterval"/> it samples the load signal and
/// nudges the global admission limit — additive-increase while healthy, halve while overloaded.
/// </summary>
internal sealed class AdaptiveAdmissionController(
    ILoadSignal signal,
    AdmissionLimit limit,
    IOptions<RateLimitingOptions> options,
    TimeProvider clock,
    ILogger<AdaptiveAdmissionController> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value.Adaptive;
        RateLimitingMeter.TrackAdmissionLimit(limit); // adaptive.limit observable gauge

        using var timer = new PeriodicTimer(o.ControlInterval, clock);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                double score;
                try
                {
                    score = signal.Sample();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Load signal sample failed; holding admission limit at {Limit}.", limit.Current);
                    continue; // fail-open: hold
                }

                var current = limit.Current;
                var next = Aimd.NextLimit(current, score, o);
                if (next != current)
                {
                    limit.Current = next;
                    logger.LogDebug("Adaptive admission limit {Old} -> {New} (load score {Score:F3}).", current, next, score);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }
}
