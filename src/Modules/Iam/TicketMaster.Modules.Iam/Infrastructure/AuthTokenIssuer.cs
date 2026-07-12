using Microsoft.Extensions.Options;
using TicketMaster.Modules.Iam.Application.Abstractions;
using TicketMaster.Modules.Iam.Application.Features.Authentication;
using TicketMaster.Modules.Iam.Domain;

namespace TicketMaster.Modules.Iam.Infrastructure;

/// <summary>
/// Issues the token pair and records the refresh token (as a hash) against the same scoped <see cref="IamDbContext"/>
/// the caller mutates — so a rotation's revoke of the old token and the add of the new one persist in one SaveChanges.
/// </summary>
public sealed class AuthTokenIssuer(
    ITokenService tokenService,
    IamDbContext db,
    IOptions<TokenIssuanceOptions> options,
    TimeProvider timeProvider) : IAuthTokenIssuer
{
    public async Task<AuthTokensResponse> IssueAsync(
        User user, string ip, CancellationToken ct, string rawRefresh = null, string refreshHash = null)
    {
        var now = timeProvider.GetUtcNow();
        var access = tokenService.CreateAccessToken(user, []); // TODO roles: later permission slice

        if (rawRefresh is null || refreshHash is null)
            (rawRefresh, refreshHash) = tokenService.CreateRefreshToken();

        var lifetime = TimeSpan.FromDays(options.Value.RefreshTokenDays);
        db.RefreshTokens.Add(RefreshToken.Issue(user.Id, refreshHash, now, lifetime, ip));
        await db.SaveChangesAsync(ct);

        var expiresIn = (int)(access.ExpiresAtUtc - now).TotalSeconds;
        return new AuthTokensResponse(access.Token, rawRefresh, "Bearer", expiresIn);
    }
}
