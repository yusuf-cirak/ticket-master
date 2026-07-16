using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace YC.BuildingBlocks.RateLimiting;

/// <summary>Composite load in [0..1] (0 = idle, 1 = saturated) plus the components that produced it, for observability.</summary>
internal readonly record struct LoadSample(double Score, double Cpu, double ThreadPool, double Latency, double Error);

/// <summary>Composite process/fleet load signal in [0..1]; 0 = idle, 1 = fully saturated.</summary>
internal interface ILoadSignal
{
    LoadSample Sample();
}

/// <summary>Last sampled load, published by the adaptive controller so the meter can expose it as gauges.</summary>
internal sealed class LoadSignalState
{
    private readonly Lock _gate = new();
    private LoadSample _last;
    public void Publish(LoadSample sample) { lock (_gate) _last = sample; }
    public LoadSample Current { get { lock (_gate) return _last; } }
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

    public LoadSample Sample()
    {
        var o = options.Value.Adaptive;
        var cores = Environment.ProcessorCount;

        var cpu = CpuUtilization(cores);
        var threadPool = LoadMath.NormalizeThreadPool(ThreadPool.PendingWorkItemCount, cores);
        var latency = LoadMath.NormalizeLatency(metrics.LatencyEwmaMs, o.LatencyBaselineMs);
        var error = Math.Clamp(metrics.ErrorRate, 0, 1);

        return new LoadSample(LoadMath.Composite(cpu, threadPool, latency, error, o), cpu, threadPool, latency, error);
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
    TimeProvider clock,
    ILogger<DistributedLoadSignal> logger) : ILoadSignal
{
    // Stable per-instance id for this node's field in the shared hash.
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    // Each field carries "{score};{unixMs}"; a node whose last write is older than the staleness window is ignored
    // (and best-effort pruned), so a crashed node no longer poisons the fleet max. The whole-hash TTL is a backstop.
    private static readonly TimeSpan HashTtl = TimeSpan.FromSeconds(30);

    private string Prefix => options.Value.KeyPrefix ?? "app";
    private RedisKey LoadKey => $"{Prefix}:load";

    public LoadSample Sample()
    {
        var localSample = local.Sample();

        if (!mux.IsConnected)
            return localSample; // fail-open

        try
        {
            var db = mux.GetDatabase();
            var nowMs = clock.GetUtcNow().ToUnixTimeMilliseconds();
            db.HashSet(LoadKey, _instanceId, $"{localSample.Score.ToString(CultureInfo.InvariantCulture)};{nowMs}");
            db.KeyExpire(LoadKey, HashTtl);

            // Ignore nodes that haven't refreshed within ~3 control intervals (min 5s).
            var staleMs = Math.Max(options.Value.Adaptive.ControlInterval.TotalMilliseconds * 3, 5_000);
            var max = localSample.Score;
            List<RedisValue> stale = null;
            foreach (var entry in db.HashGetAll(LoadKey))
            {
                if (!TryParseField(entry.Value, out var score, out var tsMs))
                    continue;
                if (nowMs - tsMs <= staleMs)
                    max = Math.Max(max, score);
                else
                    (stale ??= []).Add(entry.Name);
            }

            if (stale is { Count: > 0 })
                db.HashDelete(LoadKey, [.. stale]); // best-effort prune of dead nodes

            return localSample with { Score = Math.Clamp(max, 0, 1) };
        }
        catch (RedisException ex)
        {
            logger.LogDebug(ex, "Distributed load signal fell back to local score (Redis unavailable).");
            return localSample; // fail-open
        }
    }

    // "{score};{unixMs}"; tolerate a bare "{score}" (treated as fresh) for rolling deploys.
    private static bool TryParseField(RedisValue value, out double score, out long tsMs)
    {
        score = 0;
        tsMs = long.MaxValue; // bare/legacy → never stale
        string raw = value;
        if (string.IsNullOrEmpty(raw))
            return false;

        var sep = raw.IndexOf(';');
        var scorePart = sep < 0 ? raw : raw[..sep];
        if (!double.TryParse(scorePart, NumberStyles.Float, CultureInfo.InvariantCulture, out score))
            return false;
        if (sep >= 0)
            long.TryParse(raw.AsSpan(sep + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out tsMs);
        return true;
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
