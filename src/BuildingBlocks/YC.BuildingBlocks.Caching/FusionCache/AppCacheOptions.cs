namespace YC.BuildingBlocks.Caching.FusionCache;

/// <summary>
/// Tunables for the application cache (<see cref="FusionCacheExtensions.AddAppCache"/>). Defaults are production-safe;
/// override any via the <c>Action&lt;AppCacheOptions&gt;</c> overload. Settable (options pattern) so a code-based
/// configurator can mutate individual knobs.
/// </summary>
public sealed class AppCacheOptions
{
    /// <summary>L1 + L2 logical time-to-live for an entry.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Random jitter added to <see cref="Duration"/> to spread expiries (anti cache-stampede / herd).</summary>
    public TimeSpan JitterMaxDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Serve a stale value when the factory / L2 fails, up to <see cref="FailSafeMaxDuration"/>.</summary>
    public bool IsFailSafeEnabled { get; set; } = true;

    /// <summary>How long a stale value stays eligible for fail-safe serving past its logical expiry.</summary>
    public TimeSpan FailSafeMaxDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Soft timeout for the value factory before fail-safe / background completion kicks in.</summary>
    public TimeSpan FactorySoftTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>L2 (distributed) entry TTL; typically longer than the L1 logical <see cref="Duration"/>.</summary>
    public TimeSpan DistributedCacheDuration { get; set; } = TimeSpan.FromMinutes(10);
}
