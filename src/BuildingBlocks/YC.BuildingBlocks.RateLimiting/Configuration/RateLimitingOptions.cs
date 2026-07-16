namespace YC.BuildingBlocks.RateLimiting;

/// <summary>Rate limiting configuration, bound from the <c>RateLimiting</c> config section.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where the limiter's counters and burst-ban state live: <see cref="RateLimitingMode.Distributed"/> (Redis,
    /// shared across instances) or <see cref="RateLimitingMode.Local"/> (per-node, in-memory). Defaults to Distributed.
    /// </summary>
    public RateLimitingMode Mode { get; set; } = RateLimitingMode.Local;

    /// <summary>Redis key prefix; defaults to the application's <c>ServiceName</c> when not set.</summary>
    public string KeyPrefix { get; set; }

    /// <summary>
    /// Claim type that carries the caller's rate-limit tier (only honored from a trusted internal auth scheme).
    /// Product-neutral default; override to match your IdP's namespaced claim.
    /// </summary>
    public string TierClaim { get; set; } = RateLimitingConstants.DefaultTierClaim;

    /// <summary>
    /// Allowlist of valid API keys (supply via secret configuration). Only a caller whose <c>X-Api-Key</c> matches an
    /// entry here is granted the elevated per-key bucket + <c>authenticated</c> tier; an unrecognized key falls through
    /// to the per-IP anonymous bucket. <b>Empty (default) means no key elevates</b> — otherwise any attacker could mint
    /// unlimited fresh high-tier buckets by rotating arbitrary keys and evade rate limiting entirely.
    /// </summary>
    public HashSet<string> ApiKeys { get; set; } = [];

    /// <summary>Named policies. Only <c>burst</c> is used now.</summary>
    public Dictionary<string, PolicyDefinition> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-tier permit multipliers (e.g. anon 0.25, authenticated 1, internal 5).</summary>
    public Dictionary<string, double> Tiers { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["anon"] = 0.25,
        ["authenticated"] = 1.0,
        ["internal"] = 5.0
    };

    /// <summary>Burst-ban: exceeding the burst window arms a fixed soft-ban for <see cref="BurstBanOptions.Duration"/>.</summary>
    public BurstBanOptions BurstBan { get; set; } = new();

    /// <summary>Layer-2 adaptive admission control (system-wide AIMD concurrency gate).</summary>
    public AdaptiveOptions Adaptive { get; set; } = new();
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
    public int MinLimit { get; set; } = 10;

    /// <summary>Ceiling for the admission limit (must be ≥ <see cref="MinLimit"/>). Also the initial limit.</summary>
    public int MaxLimit { get; set; } = 1000;

    /// <summary>Additive-increase step applied each healthy control tick.</summary>
    public int Step { get; set; } = 10;

    /// <summary>How often the AIMD controller re-evaluates the load score and adjusts the limit.</summary>
    public TimeSpan ControlInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Composite score below this ⇒ additive increase.</summary>
    public double HealthyThreshold { get; set; } = 0.70;

    /// <summary>Composite score above this ⇒ multiplicative decrease.</summary>
    public double OverloadThreshold { get; set; } = 0.85;

    /// <summary>Multiplicative-decrease factor on overload (0..1). 0.7 = gentler than halving; smoother recovery.</summary>
    public double DecreaseFactor { get; set; } = 0.7;

    /// <summary>EWMA smoothing on the load score (0..1]. Lower = smoother (a single spike won't slam the limit); 1 = no smoothing.</summary>
    public double ScoreSmoothing { get; set; } = 0.4;

    /// <summary>Expected p50-ish request latency (ms); the latency signal normalizes against this.</summary>
    public int LatencyBaselineMs { get; set; } = 200;

    public double CpuWeight { get; set; } = 1;
    public double ThreadPoolWeight { get; set; } = 1;
    public double LatencyWeight { get; set; } = 1;
    public double ErrorWeight { get; set; } = 1;
}

public sealed class PolicyDefinition
{
    public int PermitLimit { get; set; }
    public TimeSpan Window { get; set; }
    /// <summary>&gt; 1 ⇒ sliding window (otherwise fixed window).</summary>
    public int SegmentsPerWindow { get; set; } = 1;
    public int QueueLimit { get; set; }
}

public sealed class BurstBanOptions
{
    /// <summary>How long a caller stays banned after exceeding the burst limit. Must be ≥ 10s (validated on start).</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(10);
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
