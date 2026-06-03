namespace TicketMaster.BuildingBlocks.RateLimiting;

/// <summary>Rate limiting configuration, bound from the <c>RateLimiting</c> config section.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; init; } = true;

    /// <summary>Redis key prefix; defaults to the application's <c>ServiceName</c> when not set.</summary>
    public string? KeyPrefix { get; set; }

    /// <summary>Named policies (e.g. <c>burst</c>, <c>quotaRead</c>, <c>quotaWrite</c>, <c>concurrency</c>).</summary>
    public Dictionary<string, PolicyDefinition> Policies { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-tier permit multipliers (e.g. anon 0.25, authenticated 1, internal 5).</summary>
    public Dictionary<string, double> Tiers { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["anon"] = 0.25,
        ["authenticated"] = 1.0,
        ["internal"] = 5.0
    };

    public SoftBanOptions SoftBan { get; init; } = new();
}

public sealed class PolicyDefinition
{
    public int PermitLimit { get; init; }
    public TimeSpan Window { get; init; }
    /// <summary>&gt; 1 ⇒ sliding window (otherwise fixed window). Ignored for the concurrency policy.</summary>
    public int SegmentsPerWindow { get; init; } = 1;
    public int QueueLimit { get; init; }
}

public sealed class SoftBanOptions
{
    public int Threshold { get; init; } = 2;
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(1);
}

public enum PrincipalTier
{
    Anonymous,
    Authenticated,
    Internal
}
