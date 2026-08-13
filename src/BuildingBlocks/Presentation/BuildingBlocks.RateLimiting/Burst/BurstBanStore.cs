using Microsoft.Extensions.Caching.Memory;

namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Tracks short-lived burst bans keyed by rate-limit partition. Exceeding the burst window arms a ban that
/// outlives the sliding window, so a caller stays 429 for the full ban duration even after the window resets.
/// </summary>
internal interface IBurstBanStore
{
    /// <summary>Remaining ban for <paramref name="key"/>, or <c>null</c> if not banned.</summary>
    Task<TimeSpan?> GetRemainingBanAsync(string key, CancellationToken ct);

    /// <summary>Bans <paramref name="key"/> for <paramref name="duration"/> (overwrites any existing ban).</summary>
    Task BanAsync(string key, TimeSpan duration, CancellationToken ct);
}

/// <summary>
/// Per-node in-memory ban store (used in <see cref="RateLimitingMode.Local"/>). Backed by <see cref="IMemoryCache"/>
/// with an absolute expiration equal to the ban duration, so an entry is evicted automatically when the ban lapses —
/// even for a one-time offender who never returns (a plain <c>ConcurrentDictionary</c> only sweeps on read, so an
/// idle expired entry would leak forever). The stored value is the ban's end instant, so <see cref="GetRemainingBanAsync"/>
/// reports the exact remaining time and treats an elapsed-but-not-yet-evicted entry as unbanned via the injected clock.
/// </summary>
internal sealed class LocalBurstBanStore(IMemoryCache cache, TimeProvider clock) : IBurstBanStore
{
    private static string CacheKey(string key) => $"{RateLimitingConstants.BanCacheKeyPrefix}{key}";

    public Task<TimeSpan?> GetRemainingBanAsync(string key, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(key), out DateTimeOffset until))
        {
            var remaining = until - clock.GetUtcNow();
            if (remaining > TimeSpan.Zero)
                return Task.FromResult<TimeSpan?>(remaining);
        }

        return Task.FromResult<TimeSpan?>(null);
    }

    public Task BanAsync(string key, TimeSpan duration, CancellationToken ct)
    {
        // Absolute expiration = ban duration → MemoryCache evicts the entry when the ban lapses (no idle leak).
        cache.Set(CacheKey(key), clock.GetUtcNow().Add(duration), absoluteExpirationRelativeToNow: duration);
        return Task.CompletedTask;
    }
}
