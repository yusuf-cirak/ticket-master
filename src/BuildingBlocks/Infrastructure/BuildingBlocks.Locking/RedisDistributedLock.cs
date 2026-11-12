using Medallion.Threading.Redis;
using StackExchange.Redis;
using BuildingBlocks.Application.Abstractions.Locking;

namespace BuildingBlocks.Locking;

/// <summary>
/// Redis-backed <see cref="IDistributedLock"/>, delegating to <c>Medallion.Threading</c>'s RedLock implementation
/// (package <c>DistributedLock.Redis</c>) rather than a hand-rolled Lua-script lock: a non-blocking try-acquire
/// (<c>timeout: TimeSpan.Zero</c> — immediate <c>null</c> if already held, matching this interface's contract) whose
/// returned handle auto-renews the TTL in the background for as long as it's held, and releases on dispose.
/// </summary>
public sealed class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    public async ValueTask<IAsyncDisposable> AcquireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        var @lock = new Medallion.Threading.Redis.RedisDistributedLock(
            $"lock:{key}", redis.GetDatabase(), o => o.Expiry(expiration));

        return await @lock.TryAcquireAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
    }
}
