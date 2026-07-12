using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace TicketMaster.BuildingBlocks.RateLimiting;

/// <summary>Composite process/fleet load score in [0..1]; 0 = idle, 1 = fully saturated.</summary>
internal interface ILoadSignal
{
    double Sample();
}

/// <summary>Pure normalization/composition helpers — kept static so they're unit-testable without any process state.</summary>
internal static class LoadMath
{
    /// <summary>ThreadPool backlog normalized against a cores×100 ceiling.</summary>
    public static double NormalizeThreadPool(long pendingWorkItems, int cores)
    {
        if (cores <= 0) return 0;
        return Math.Clamp(pendingWorkItems / (double)(cores * 100), 0, 1);
    }

    /// <summary>Observed latency as a ratio of baseline; 2× baseline (or worse) reads as fully saturated.</summary>
    public static double NormalizeLatency(double ewmaMs, double baselineMs)
    {
        if (baselineMs <= 0) return 0;
        return Math.Clamp(ewmaMs / (baselineMs * 2), 0, 1);
    }

    /// <summary>Weighted average of the four signals, clamped to [0..1].</summary>
    public static double Composite(double cpu, double threadPool, double latency, double error, AdaptiveOptions o)
    {
        var totalWeight = o.CpuWeight + o.ThreadPoolWeight + o.LatencyWeight + o.ErrorWeight;
        if (totalWeight <= 0) return 0;

        var score = (cpu * o.CpuWeight
                     + threadPool * o.ThreadPoolWeight
                     + latency * o.LatencyWeight
                     + error * o.ErrorWeight) / totalWeight;

        return Math.Clamp(score, 0, 1);
    }
}

/// <summary>
/// Local-process load score: CPU utilization over the last interval, ThreadPool backlog, request-latency EWMA and
/// 5xx error rate, weighted and clamped to [0..1].
/// </summary>
internal sealed class LocalLoadSignal(
    RequestMetricsCollector metrics,
    IOptions<RateLimitingOptions> options,
    TimeProvider clock) : ILoadSignal
{
    private readonly object _cpuLock = new();
    private long _lastTimestamp = clock.GetTimestamp();
    private TimeSpan _lastCpu = Process.GetCurrentProcess().TotalProcessorTime;

    public double Sample()
    {
        var o = options.Value.Adaptive;
        var cores = Environment.ProcessorCount;

        var cpu = CpuUtilization(cores);
        var threadPool = LoadMath.NormalizeThreadPool(ThreadPool.PendingWorkItemCount, cores);
        var latency = LoadMath.NormalizeLatency(metrics.LatencyEwmaMs, o.LatencyBaselineMs);
        var error = Math.Clamp(metrics.ErrorRate, 0, 1);

        return LoadMath.Composite(cpu, threadPool, latency, error, o);
    }

    // Process CPU time consumed since the last sample, divided by (wall-clock elapsed × cores). Clamped to [0..1].
    private double CpuUtilization(int cores)
    {
        lock (_cpuLock)
        {
            var now = clock.GetTimestamp();
            var cpuNow = Process.GetCurrentProcess().TotalProcessorTime;

            var wallSeconds = clock.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
            var cpuSeconds = (cpuNow - _lastCpu).TotalSeconds;

            _lastTimestamp = now;
            _lastCpu = cpuNow;

            if (wallSeconds <= 0 || cores <= 0) return 0;
            return Math.Clamp(cpuSeconds / (wallSeconds * cores), 0, 1);
        }
    }
}

/// <summary>
/// Fleet-aware load score: publishes this node's local score into a Redis hash and returns the fleet MAX (worst node),
/// so every node adapts to the whole fleet's health. FAILS OPEN — on any Redis error or a disconnected multiplexer it
/// returns the local score, so a Redis outage never blocks admission control.
/// </summary>
internal sealed class DistributedLoadSignal(
    LocalLoadSignal local,
    IConnectionMultiplexer mux,
    IOptions<RateLimitingOptions> options,
    ILogger<DistributedLoadSignal> logger) : ILoadSignal
{
    // Stable per-instance id for this node's field in the shared hash.
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    // ponytail: whole-hash TTL (not per-field HEXPIRE) — a node that dies without clearing its field lingers in the
    // "fleet max" until every node stops refreshing and the hash expires. Acceptable for a load SIGNAL; upgrade to
    // Redis 7.4 HEXPIRE per-field TTL if stale-node skew ever matters.
    private static readonly TimeSpan HashTtl = TimeSpan.FromSeconds(10);

    private string Prefix => options.Value.KeyPrefix ?? "app";
    private RedisKey LoadKey => $"{Prefix}:load";

    public double Sample()
    {
        var localScore = local.Sample();

        if (!mux.IsConnected)
            return localScore; // fail-open

        try
        {
            var db = mux.GetDatabase();
            db.HashSet(LoadKey, _instanceId, localScore);
            db.KeyExpire(LoadKey, HashTtl); // refresh liveness on every sample

            var max = localScore;
            foreach (var entry in db.HashGetAll(LoadKey))
                if (entry.Value.TryParse(out double v) && v > max)
                    max = v;

            return Math.Clamp(max, 0, 1);
        }
        catch (RedisException ex)
        {
            logger.LogDebug(ex, "Distributed load signal fell back to local score (Redis unavailable).");
            return localScore; // fail-open
        }
    }
}

/// <summary>
/// Records per-request latency (EWMA) and 5xx error rate (rolling over the last <see cref="WindowSize"/> requests).
/// Fed by the timing middleware; read by <see cref="LocalLoadSignal"/>.
/// </summary>
internal sealed class RequestMetricsCollector
{
    private const int WindowSize = 1000;
    private const double Alpha = 0.2; // EWMA smoothing

    private readonly object _lock = new();
    private readonly byte[] _errorRing = new byte[WindowSize]; // 1 = 5xx, 0 = ok
    private int _ringIndex;
    private int _filled;
    private int _errorSum;
    private double _latencyEwmaMs;
    private bool _seenAny;

    public void Record(double durationMs, int statusCode)
    {
        var is5xx = (byte)(statusCode >= 500 ? 1 : 0);

        lock (_lock)
        {
            _latencyEwmaMs = _seenAny ? Alpha * durationMs + (1 - Alpha) * _latencyEwmaMs : durationMs;
            _seenAny = true;

            _errorSum += is5xx - _errorRing[_ringIndex];
            _errorRing[_ringIndex] = is5xx;
            _ringIndex = (_ringIndex + 1) % WindowSize;
            if (_filled < WindowSize) _filled++;
        }
    }

    public double LatencyEwmaMs
    {
        get { lock (_lock) return _latencyEwmaMs; }
    }

    /// <summary>5xx fraction over the rolling window, in [0..1].</summary>
    public double ErrorRate
    {
        get { lock (_lock) return _filled == 0 ? 0 : (double)_errorSum / _filled; }
    }
}
