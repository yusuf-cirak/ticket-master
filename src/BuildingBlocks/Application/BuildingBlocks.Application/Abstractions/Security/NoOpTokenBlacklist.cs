
namespace BuildingBlocks.Application.Abstractions.Security;

/// <summary>
/// Default <see cref="ITokenBlacklist"/> so the sensitive-request behavior always resolves. Treats nothing as
/// revoked; the Redis-backed blacklist overrides it wherever revocation must actually be enforced.
/// </summary>
public sealed class NoOpTokenBlacklist : ITokenBlacklist
{
    public Task RevokeAsync(string tokenId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
