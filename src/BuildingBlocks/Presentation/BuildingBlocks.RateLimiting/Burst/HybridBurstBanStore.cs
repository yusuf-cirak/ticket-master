#nullable enable
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Distributed burst-ban store that keeps the per-request read path OFF Redis. Reads consult a per-node
/// <see cref="IMemoryCache"/> mirror only — a not-banned caller (the common case) costs zero Redis ops. A ban is
/// broadcast over Redis pub/sub (channel <c>{prefix}:ban:events</c>) so every node mirrors it into its own local
/// cache; it is also written as a TTL key for durability/observability (never read on the hot path). FAILS OPEN — a
/// Redis/pub-sub outage is never a hard 429 and never blocks; the ban still applies on the node that armed it.
/// </summary>
/// <remarks>
/// ponytail: pub/sub is best-effort. A dropped message, or a node that joins after a ban was armed, may not see that
/// ban until it is re-armed. Bans are short (≥10s) and the limiter is fail-open by design, so this is acceptable; the
/// upgrade path (if strict cross-node bans are ever needed) is a startup SCAN of the ban keyspace into the mirror.
/// </remarks>
internal sealed class HybridBurstBanStore(
    IMemoryCache cache,
    IConnectionMultiplexer mux,
    IOptions<RateLimitingOptions> options,
    TimeProvider clock) : IBurstBanStore
{
    private string Prefix => options.Value.KeyPrefix ?? "app";
    private RedisChannel Channel => RedisChannel.Literal($"{Prefix}{RateLimitingConstants.BanChannelSuffix}");
    private RedisKey BanKey(string key) => $"{Prefix}:ban:{key}";
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

    public async Task BanAsync(string key, TimeSpan duration, CancellationToken ct)
    {
        var until = clock.GetUtcNow().Add(duration);
        Mirror(key, until); // enforce on this node immediately, independent of Redis

        if (!mux.IsConnected)
            return; // fail-open — the local mirror already holds the ban

        try
        {
            // Durability/observability only: presence ⇒ banned, PTTL ⇒ remaining. The hot path never reads this.
            await mux.GetDatabase().StringSetAsync(BanKey(key), "1", duration);
            // Broadcast so every node mirrors the ban into its own local cache.
            await mux.GetSubscriber().PublishAsync(Channel, Encode(key, until));
        }
        catch (RedisException)
        {
            // fail-open — the local mirror already holds the ban
        }
    }

    /// <summary>Applies a (possibly remote) ban to the local cache. Absolute expiration = the remaining window, so the
    /// entry self-evicts (no idle leak) and an elapsed-but-unevicted entry reads as unbanned via the clock.</summary>
    internal void Mirror(string key, DateTimeOffset until)
    {
        var remaining = until - clock.GetUtcNow();
        if (remaining > TimeSpan.Zero)
            cache.Set(CacheKey(key), until, absoluteExpirationRelativeToNow: remaining);
    }

    // "{untilUtcTicks}\n{key}" — the key contains '|' but never '\n', so the first line splits cleanly.
    internal static RedisValue Encode(string key, DateTimeOffset until) => $"{until.UtcTicks}\n{key}";

    internal static bool TryDecode(RedisValue value, out string key, out DateTimeOffset until)
    {
        key = string.Empty;
        until = default;
        string? s = value;
        if (string.IsNullOrEmpty(s))
            return false;

        var nl = s.IndexOf('\n');
        if (nl <= 0 || nl == s.Length - 1)
            return false;
        if (!long.TryParse(s.AsSpan(0, nl), out var ticks))
            return false;

        key = s[(nl + 1)..];
        until = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }
}
