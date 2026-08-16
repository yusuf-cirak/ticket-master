using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketMaster.Modules.Iam.Application.Abstractions;
using TicketMaster.Modules.Iam.Application.Features.Authentication;
using TicketMaster.Modules.Iam.Domain;
using TicketMaster.Modules.Iam.Infrastructure;
using BuildingBlocks.Web;
using YC.Monad;

namespace TicketMaster.Modules.Iam.Application.Features.Authentication.Commands;

/// <summary>
/// Exchanges an active refresh token for a fresh pair, rotating (revoking) the presented token. Re-checks lockout so a
/// locked-out account cannot keep minting access tokens via refresh. Any failure returns the generic 401.
/// </summary>
public static class RefreshCommand
{
    public sealed record Command(string RefreshToken) : IRequest<Result<AuthTokensResponse>>;

    public sealed class Handler(
        ITokenService tokenService,
        IamDbContext db,
        UserManager<User> userManager,
        IAuthTokenIssuer issuer,
        TimeProvider timeProvider,
        IHttpContextAccessor httpContextAccessor) : IRequestHandler<Command, Result<AuthTokensResponse>>
    {
        public async ValueTask<Result<AuthTokensResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();
            var hash = tokenService.HashRefreshToken(request.RefreshToken ?? string.Empty);

            // Tracked: it's rotated (revoked) below, and the context defaults to no-tracking.
            var existing = await db.RefreshTokens.AsTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
            if (existing is null || !existing.IsActive(now))
                return AuthErrors.InvalidCredentials;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == existing.UserId, cancellationToken);
            if (user is null || await userManager.IsLockedOutAsync(user))
                return AuthErrors.InvalidCredentials;

            // Rotate: revoke the presented token, pointing it at its replacement, then mint a fresh pair.
            var (rawRefresh, refreshHash) = tokenService.CreateRefreshToken();
            existing.Revoke(now, refreshHash);

            var ip = httpContextAccessor.HttpContext?.GetClientIpAddress() ?? string.Empty;
            var tokens = await issuer.IssueAsync(user, ip, cancellationToken, rawRefresh, refreshHash);
            return Result.Success(tokens);
        }
    }
}
