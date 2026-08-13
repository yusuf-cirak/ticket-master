using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using BuildingBlocks.Application.Abstractions.Security;

namespace BuildingBlocks.Locking;

/// <summary>
/// Redis-backed <see cref="ITokenBlacklist"/>. Stores <c>revoked:{jti}</c> with a TTL equal to the token's remaining
/// lifetime, so revocations self-expire. Fails open (treats as not-revoked, logs a warning) if Redis is unreachable —
/// availability over strict revocation, matching the rest of the Redis-dependent path.
/// </summary>
public sealed class RedisTokenBlacklist(IConnectionMultiplexer redis, ILogger<RedisTokenBlacklist> logger) : ITokenBlacklist
{
    private static string Key(string tokenId) => $"revoked:{tokenId}";

    public async Task RevokeAsync(string tokenId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        var ttl = expiresAtUtc - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
            return; // already expired — nothing to revoke

        await redis.GetDatabase().StringSetAsync(Key(tokenId), "1", ttl);
    }

    public async Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await redis.GetDatabase().KeyExistsAsync(Key(tokenId));
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Token blacklist check failed; failing open (treating token {TokenId} as not revoked)", tokenId);
            return false;
        }
    }
}
