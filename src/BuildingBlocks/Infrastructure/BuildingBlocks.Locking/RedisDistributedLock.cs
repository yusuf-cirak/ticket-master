using StackExchange.Redis;
using BuildingBlocks.Application.Abstractions.Locking;

namespace BuildingBlocks.Locking;

/// <summary>
/// Redis-backed <see cref="IDistributedLock"/>. Acquires with <c>SET key token NX PX</c> (atomic, non-blocking) and
/// releases with a compare-and-delete Lua script so a lock is only released by its owner — never one whose TTL had
/// already expired and been re-acquired by someone else.
/// </summary>
public sealed class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    public async ValueTask<IAsyncDisposable> AcquireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        var redisKey = $"lock:{key}";
        var token = Guid.NewGuid().ToString("N");

        var acquired = await db.StringSetAsync(redisKey, token, expiration, When.NotExists);
        return acquired ? new Handle(db, redisKey, token) : null;
    }

    private sealed class Handle(IDatabase db, string key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]);
    }
}
