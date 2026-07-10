namespace TicketMaster.BuildingBlocks.Domain.Security;

/// <summary>
/// Revocation list for still-valid-but-cancelled access tokens (logout / forced sign-out). A token is revoked by its
/// <c>jti</c> until its natural expiry, after which the entry can be dropped. JWT validation consults this so a
/// revoked token is rejected even though its signature and lifetime are still good.
/// </summary>
public interface ITokenBlacklist
{
    /// <summary>Revokes a token id until <paramref name="expiresAtUtc"/> (its own expiry — no need to keep it longer).</summary>
    Task RevokeAsync(string tokenId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default);

    /// <summary>True if the token id was revoked. Implementations fail open (return false) on backing-store errors.</summary>
    Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken = default);
}
