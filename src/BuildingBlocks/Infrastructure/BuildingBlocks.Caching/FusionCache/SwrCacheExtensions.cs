using ZiggyCreatures.Caching.Fusion;

namespace BuildingBlocks.Caching.FusionCache;

/// <summary>
/// Stale-while-revalidate reads over FusionCache, in the ergonomic soft/hard-TTL shape. This is the primitive to reach
/// for instead of hand-rolling a Redis SWR entry: FusionCache already gives you what the hand-rolled version can't do
/// safely — <b>single-flight factory coalescing</b> (a thundering herd of concurrent callers for one key runs the
/// loader exactly once, the rest await that result), plus fail-safe stale serving and a proactive background refresh.
/// </summary>
public static class SwrCacheExtensions
{
    /// <summary>
    /// Reads <paramref name="key"/>, running <paramref name="factory"/> only when needed:
    /// <list type="bullet">
    /// <item>fresh (within <paramref name="softTtl"/>) → return the cached value;</item>
    /// <item>fresh but past the eager-refresh point → return the cached value <em>and</em> refresh in the background;</item>
    /// <item>stale (past <paramref name="softTtl"/>, within <paramref name="hardTtl"/>) → serve the stale value fast
    /// (fail-safe + factory soft-timeout) while the refresh completes in the background;</item>
    /// <item>hard-expired (past <paramref name="hardTtl"/>) → run the factory and wait.</item>
    /// </list>
    /// Concurrent callers for the same key never stampede the factory — they share the one in-flight execution.
    /// </summary>
    /// <param name="softTtl">Fresh window. After it the value is stale but still served while it refreshes.</param>
    /// <param name="hardTtl">Absolute lifetime; must be &gt;= <paramref name="softTtl"/>. The extra span is the fail-safe window.</param>
    /// <param name="eagerRefreshThreshold">Fraction (0..1) of <paramref name="softTtl"/> after which a read refreshes in the background. 0 disables it.</param>
    public static ValueTask<T> GetOrSetSwrAsync<T>(
        this IFusionCache cache,
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan softTtl,
        TimeSpan hardTtl,
        float eagerRefreshThreshold = 0.8f,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);
        if (hardTtl < softTtl)
            throw new ArgumentOutOfRangeException(nameof(hardTtl), "hardTtl must be greater than or equal to softTtl.");

        var options = new FusionCacheEntryOptions(softTtl)
            .SetFailSafe(true, hardTtl - softTtl);

        if (eagerRefreshThreshold is > 0f and < 1f)
            options.SetEagerRefresh(eagerRefreshThreshold);

        return cache.GetOrSetAsync(key, factory, options, token);
    }
}
