namespace BuildingBlocks.Caching.FusionCache;

/// <summary>
/// Tuning for the app cache (<see cref="FusionCacheExtensions.AddAppCache"/>). Bound from the <c>Cache</c> config
/// section and then overridable in code via the <c>Action&lt;AppCacheOptions&gt;</c> callback. Defaults enable the full
/// anti-stampede / stale-while-revalidate behavior FusionCache provides — factory-call coalescing (single-flight) is
/// always on; the knobs below add duration jitter, fail-safe, factory timeouts and eager background refresh.
/// </summary>
public sealed class AppCacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>L1 + L2 logical fresh window. Past it the entry is stale but fail-safe can still serve it.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Random spread added per entry so keys don't all expire at once (anti herd / cache stampede).</summary>
    public TimeSpan JitterMaxDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Serve the last good value when the factory throws or times out, instead of surfacing the error.</summary>
    public bool IsFailSafeEnabled { get; set; } = true;

    /// <summary>How long past expiry a stale value may still be served under fail-safe.</summary>
    public TimeSpan FailSafeMaxDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>After a fail-safe activation, wait at least this long before running the factory again.</summary>
    public TimeSpan FailSafeThrottleDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Return the stale value this fast when the factory is slow, finishing the refresh in the background.</summary>
    public TimeSpan FactorySoftTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Hard cap on a factory call; with fail-safe on, exceeding it serves stale rather than blocking.</summary>
    public TimeSpan FactoryHardTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Stale-while-revalidate: a request arriving after this fraction (0..1) of <see cref="Duration"/> has elapsed
    /// triggers a background refresh and returns the still-fresh value immediately — so no caller ever waits for a
    /// refresh, and only one refresh runs. 0 disables it. 0.8 = refresh in the last 20% of the window.
    /// </summary>
    public float EagerRefreshThreshold { get; set; } = 0.8f;

    /// <summary>Don't block the caller on L2 (Redis) writes/backplane notifications; run them in the background.</summary>
    public bool AllowBackgroundDistributedCacheOperations { get; set; } = true;

    /// <summary>L2 (Redis) physical TTL; usually &gt;= <see cref="Duration"/>.</summary>
    public TimeSpan DistributedCacheDuration { get; set; } = TimeSpan.FromMinutes(10);
}
