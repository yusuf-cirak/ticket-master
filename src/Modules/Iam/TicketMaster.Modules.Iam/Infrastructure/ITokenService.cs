using TicketMaster.Modules.Iam.Domain;

namespace TicketMaster.Modules.Iam.Infrastructure;

/// <summary>A minted access token: the signed JWT, its <c>jti</c> (for revocation) and its absolute expiry.</summary>
public sealed record AccessToken(string Token, string Jti, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Issues the module's own JWTs and opaque refresh tokens. The access token is signed so the shared bearer validator
/// (<c>AddJwtAuthentication</c>) accepts it; the refresh token is a random secret whose SHA-256 hash is all that is
/// ever stored.
/// </summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(User user, IEnumerable<string> roles);

    /// <summary>Generates a new refresh token; returns the raw value (given to the caller once) and its stored hash.</summary>
    (string Raw, string Hash) CreateRefreshToken();

    /// <summary>Hashes a raw refresh token the same way it is stored, for lookup on /refresh and /logout.</summary>
    string HashRefreshToken(string raw);
}
