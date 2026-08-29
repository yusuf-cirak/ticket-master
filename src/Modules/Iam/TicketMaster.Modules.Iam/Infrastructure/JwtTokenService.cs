using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TicketMaster.Modules.Iam.Domain;
using BuildingBlocks.Security;
using BuildingBlocks.Web;

namespace TicketMaster.Modules.Iam.Infrastructure;

/// <summary>
/// HMAC-SHA256 JWT issuer whose tokens the shared bearer validator accepts (same Issuer/Audience/SigningKey).
/// ponytail: only <see cref="JwtMode.Symmetric"/> is supported — this issuer is the dev/test signing path; production
/// federates to an external authority, which mints its own tokens.
/// </summary>
public sealed class JwtTokenService(
    IOptions<JwtOptions> jwtOptions,
    IOptions<TokenIssuanceOptions> tokenOptions,
    TimeProvider timeProvider) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken CreateAccessToken(User user, IEnumerable<string> roles)
    {
        var jwt = jwtOptions.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(tokenOptions.Value.AccessTokenMinutes);
        var jti = Guid.CreateVersion7().ToString("N");

        // MapInboundClaims=false on the validator, so claim types stay raw ("sub"/"jti"/"role"/"scope").
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = user.Id.Value.ToString(),
            ["jti"] = jti,
            ["email"] = user.Email ?? string.Empty,
            ["name"] = user.DisplayName,
            ["scope"] = "iam:user"
        };

        var roleList = roles as string[] ?? roles?.ToArray() ?? [];
        if (roleList.Length > 0)
            claims["role"] = roleList; // serialized as a JSON array → one "role" claim per element on read

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(Handler.CreateToken(descriptor), jti, expires);
    }

    public (string Raw, string Hash) CreateRefreshToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (raw, HashRefreshToken(raw));
    }

    // Hashed on every issue, rotation and lookup; `raw` is client-supplied on the validation path, so guard the stack
    // buffer with a pooled fallback. Same bytes as before → identical base64, so stored hashes stay comparable.
    public string HashRefreshToken(string raw)
    {
        var maxBytes = Encoding.UTF8.GetMaxByteCount(raw.Length);
        var rented = maxBytes > 256 ? ArrayPool<byte>.Shared.Rent(maxBytes) : null;
        Span<byte> utf8 = rented ?? stackalloc byte[256];
        var written = Encoding.UTF8.GetBytes(raw, utf8);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(utf8[..written], hash);

        if (rented is not null)
            ArrayPool<byte>.Shared.Return(rented);

        return Convert.ToBase64String(hash);
    }
}
