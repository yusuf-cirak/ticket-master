namespace TicketMaster.BuildingBlocks.RateLimiting;

/// <summary>Rate limiting configuration, bound from the <c>RateLimiting</c> config section.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Where the limiter's counters and burst-ban state live: <see cref="RateLimitingMode.Distributed"/> (Redis,
    /// shared across instances) or <see cref="RateLimitingMode.Local"/> (per-node, in-memory). Defaults to Distributed.
    /// </summary>
    public RateLimitingMode Mode { get; init; } = RateLimitingMode.Distributed;

    /// <summary>Redis key prefix; defaults to the application's <c>ServiceName</c> when not set.</summary>
    public string KeyPrefix { get; set; }

    /// <summary>Named policies. Only <c>burst</c> is used now.</summary>
    public Dictionary<string, PolicyDefinition> Policies { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-tier permit multipliers (e.g. anon 0.25, authenticated 1, internal 5).</summary>
    public Dictionary<string, double> Tiers { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["anon"] = 0.25,
        ["authenticated"] = 1.0,
        ["internal"] = 5.0
    };

    /// <summary>Burst-ban: exceeding the burst window arms a fixed soft-ban for <see cref="BurstBanOptions.Duration"/>.</summary>
    public BurstBanOptions BurstBan { get; init; } = new();

    /// <summary>Layer-2 adaptive admission control (system-wide AIMD concurrency gate).</summary>
    public AdaptiveOptions Adaptive { get; init; } = new();
}

/// <summary>
/// Layer 2 — system-wide adaptive admission control. A background AIMD controller adjusts a single global admission
/// limit (max concurrent in-flight admitted requests) from a composite health score; an in-process concurrency gate
/// enforces it. Enforcement is ALWAYS in-process (a true distributed in-flight counter leaks on crashes). The two
/// <see cref="RateLimitingMode"/>s differ ONLY in the load SIGNAL: <see cref="RateLimitingMode.Local"/> scores the
/// local process; <see cref="RateLimitingMode.Distributed"/> aggregates every node's score via Redis (fleet max),
/// so each node adapts to the whole fleet's health.
/// </summary>
public sealed class AdaptiveOptions
{
    /// <summary>Floor for the admission limit (must be ≥ 1).</summary>
    public int MinLimit { get; init; } = 10;

    /// <summary>Ceiling for the admission limit (must be ≥ <see cref="MinLimit"/>). Also the initial limit.</summary>
    public int MaxLimit { get; init; } = 1000;

    /// <summary>Additive-increase step applied each healthy control tick.</summary>
    public int Step { get; init; } = 10;

    /// <summary>How often the AIMD controller re-evaluates the load score and adjusts the limit.</summary>
    public TimeSpan ControlInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Composite score below this ⇒ additive increase.</summary>
    public double HealthyThreshold { get; init; } = 0.70;

    /// <summary>Composite score above this ⇒ multiplicative decrease (halve).</summary>
    public double OverloadThreshold { get; init; } = 0.85;

    /// <summary>Expected p50-ish request latency (ms); the latency signal normalizes against this.</summary>
    public int LatencyBaselineMs { get; init; } = 200;

    public double CpuWeight { get; init; } = 1;
    public double ThreadPoolWeight { get; init; } = 1;
    public double LatencyWeight { get; init; } = 1;
    public double ErrorWeight { get; init; } = 1;
}

public sealed class PolicyDefinition
{
    public int PermitLimit { get; init; }
    public TimeSpan Window { get; init; }
    /// <summary>&gt; 1 ⇒ sliding window (otherwise fixed window).</summary>
    public int SegmentsPerWindow { get; init; } = 1;
    public int QueueLimit { get; init; }
}

public sealed class BurstBanOptions
{
    /// <summary>How long a caller stays banned after exceeding the burst limit. Must be ≥ 10s (validated on start).</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(10);
}

public enum RateLimitingMode
{
    Local,
    Distributed
}

public enum PrincipalTier
{
    Anonymous,
    Authenticated,
    Internal
}
