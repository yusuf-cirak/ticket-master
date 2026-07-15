using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace YC.BuildingBlocks.RateLimiting;

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

/// <summary>Per-node in-memory ban store (used in <see cref="RateLimitingMode.Local"/>).</summary>
internal sealed class LocalBurstBanStore(TimeProvider clock) : IBurstBanStore
{
    private readonly ConcurrentDictionary<string, long> _bannedUntil = new();

    public Task<TimeSpan?> GetRemainingBanAsync(string key, CancellationToken ct)
    {
        if (_bannedUntil.TryGetValue(key, out var untilTicks))
        {
            var remaining = new DateTimeOffset(untilTicks, TimeSpan.Zero) - clock.GetUtcNow();
            if (remaining > TimeSpan.Zero)
                return Task.FromResult<TimeSpan?>(remaining);

            _bannedUntil.TryRemove(key, out _); // expired — sweep it
        }

        return Task.FromResult<TimeSpan?>(null);
    }

    public Task BanAsync(string key, TimeSpan duration, CancellationToken ct)
    {
        _bannedUntil[key] = clock.GetUtcNow().Add(duration).UtcTicks;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Redis-backed ban store (used in <see cref="RateLimitingMode.Distributed"/>). The ban is a plain key with a TTL:
/// presence ⇒ banned, PTTL ⇒ remaining. FAILS OPEN — a Redis outage never turns into a hard 429 (GetRemaining
/// returns "not banned", Ban is a no-op).
/// </summary>
internal sealed class RedisBurstBanStore(IConnectionMultiplexer mux, IOptions<RateLimitingOptions> options) : IBurstBanStore
{
    private string Prefix => options.Value.KeyPrefix ?? "app";

    private RedisKey BanKey(string key) => $"{Prefix}:ban:{key}";

    public async Task<TimeSpan?> GetRemainingBanAsync(string key, CancellationToken ct)
    {
        if (!mux.IsConnected)
            return null; // fail-open

        try
        {
            var ttl = await mux.GetDatabase().KeyTimeToLiveAsync(BanKey(key));
            return ttl is { } t && t > TimeSpan.Zero ? t : null;
        }
        catch (RedisException)
        {
            return null; // fail-open
        }
    }

    public async Task BanAsync(string key, TimeSpan duration, CancellationToken ct)
    {
        if (!mux.IsConnected)
            return; // fail-open

        try
        {
            // Overwrite so a repeat breach re-arms the full duration.
            await mux.GetDatabase().StringSetAsync(BanKey(key), "1", duration);
        }
        catch (RedisException)
        {
            // fail-open — no-op
        }
    }
}
