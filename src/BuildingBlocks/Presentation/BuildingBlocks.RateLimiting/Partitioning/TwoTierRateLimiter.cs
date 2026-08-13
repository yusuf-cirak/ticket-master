#nullable enable
using System.Threading.RateLimiting;
using RedisRateLimiting;
using StackExchange.Redis;

namespace BuildingBlocks.RateLimiting;

/// <summary>
/// A two-tier limiter for one partition key: a per-node in-memory limiter (cap = the global limit) in front of the
/// Redis limiter. Normal traffic clears the local tier cheaply and is then counted authoritatively in Redis; a
/// single-node flood is rejected by the local tier WITHOUT a Redis round-trip (the point of rate limiting — an
/// attack — stops hammering Redis). Because the local cap equals the global limit, the local tier never rejects
/// traffic Redis would have allowed: a single node alone exceeding the global limit is a fleet-wide breach Redis
/// would 429 anyway. Redis stays the source of truth for the true cross-node count.
/// </summary>
internal sealed class TwoTierRateLimiter : RateLimiter
{
    private readonly RateLimiter _local;
    private readonly RateLimiter _redis;

    public TwoTierRateLimiter(ResolvedPolicy policy, string key, IConnectionMultiplexer mux)
        : this(BuildLocal(policy), BuildRedis(policy, key, mux)) { }

    // Injectable seam for tests (fake the Redis tier without a live connection).
    internal TwoTierRateLimiter(RateLimiter local, RateLimiter redis)
    {
        _local = local;
        _redis = redis;
    }

    private static RateLimiter BuildLocal(ResolvedPolicy policy) =>
        policy.SegmentsPerWindow > 1
            ? new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = policy.PermitLimit,
                Window = policy.Window,
                SegmentsPerWindow = policy.SegmentsPerWindow,
                QueueLimit = 0
            })
            : new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = policy.PermitLimit,
                Window = policy.Window,
                QueueLimit = 0
            });

    private static RateLimiter BuildRedis(ResolvedPolicy policy, string key, IConnectionMultiplexer mux) =>
        policy.SegmentsPerWindow > 1
            ? new RedisSlidingWindowRateLimiter<string>(key, new RedisSlidingWindowRateLimiterOptions
            {
                ConnectionMultiplexerFactory = () => mux,
                PermitLimit = policy.PermitLimit,
                Window = policy.Window
            })
            : new RedisFixedWindowRateLimiter<string>(key, new RedisFixedWindowRateLimiterOptions
            {
                ConnectionMultiplexerFactory = () => mux,
                PermitLimit = policy.PermitLimit,
                Window = policy.Window
            });

    // Keep the partition alive while EITHER tier is active (the more-recently-used idle time drives cleanup).
    public override TimeSpan? IdleDuration =>
        _local.IdleDuration is { } l && _redis.IdleDuration is { } r ? (l < r ? l : r) : null;

    public override RateLimiterStatistics? GetStatistics() => _redis.GetStatistics();

    // The HTTP middleware uses the async path; the sync path can only do the in-memory tier (no Redis from a sync call).
    protected override RateLimitLease AttemptAcquireCore(int permitCount) => _local.AttemptAcquire(permitCount);

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        var localLease = _local.AttemptAcquire(permitCount);
        if (!localLease.IsAcquired)
            return localLease; // local flood shield — reject without a Redis round-trip (lease carries RetryAfter)

        // Redis is authoritative. A local permit spent here that Redis then rejects is harmless: local cap == global,
        // so the local counter only ever leads the Redis counter, and it decays with the window.
        return await _redis.AcquireAsync(permitCount, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;
        _local.Dispose();
        _redis.Dispose();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _local.DisposeAsync();
        await _redis.DisposeAsync();
    }
}
