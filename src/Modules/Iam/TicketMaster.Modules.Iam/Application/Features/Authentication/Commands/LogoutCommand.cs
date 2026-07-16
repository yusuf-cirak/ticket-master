using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using YC.BuildingBlocks.Domain.Security;
using TicketMaster.Modules.Iam.Infrastructure;
using YC.Monad;

namespace TicketMaster.Modules.Iam.Application.Features.Authentication.Commands;

/// <summary>
/// Signs the caller out: revokes the presented refresh token (only if the caller owns it) and blacklists the current
/// access token's jti until its natural expiry so it is rejected before it would otherwise expire.
/// </summary>
public static class LogoutCommand
{
    public sealed record Command(string RefreshToken) : IRequest<Result>;

    public sealed class Handler(
        ITokenBlacklist blacklist,
        ICurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
        IamDbContext db,
        ITokenService tokenService,
        TimeProvider timeProvider) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();

            // Revoke the presented refresh token if we still hold an active copy — but only its owner may revoke it.
            var hash = tokenService.HashRefreshToken(request.RefreshToken ?? string.Empty);
            var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
            if (existing is not null && existing.IsActive(now) && existing.UserId.Value.ToString() == currentUser.UserId)
                existing.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);

            // Blacklist the current access token so it is rejected before its natural expiry.
            if (currentUser.TokenId.TryGetValue(out var jti))
            {
                var principal = httpContextAccessor.HttpContext?.User;
                var exp = long.TryParse(principal?.FindFirstValue("exp"), out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds(unix)
                    : now;
                await blacklist.RevokeAsync(jti, exp, cancellationToken);
            }

            return Result.Success();
        }
    }
}
