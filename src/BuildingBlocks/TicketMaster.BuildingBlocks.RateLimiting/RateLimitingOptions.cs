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
