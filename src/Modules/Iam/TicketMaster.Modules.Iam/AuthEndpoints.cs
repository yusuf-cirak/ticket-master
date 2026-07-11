using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketMaster.BuildingBlocks.Domain.Security;
using TicketMaster.BuildingBlocks.Domain.ValueObjects;
using TicketMaster.Modules.Iam.Domain;
using TicketMaster.Modules.Iam.Infrastructure;
using TicketMaster.ServiceDefaults;

namespace TicketMaster.Modules.Iam;

/// <summary>
/// Authentication endpoints for the IAM module: register / login / refresh / logout / me. Direct endpoints (no
/// MediatR) — they call <see cref="UserManager{TUser}"/>, <see cref="ITokenService"/> and <see cref="IamDbContext"/>
/// directly. Credential failures return a generic 401 so account existence and lockout state are not leaked.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapPost("/register", RegisterAsync).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapGet("/me", (ICurrentUser currentUser) => Results.Ok(new { userId = currentUser.UserId }))
            .RequireAuthorization();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest body, UserManager<User> userManager)
    {
        var emailResult = Email.Create(body.Email);
        if (!emailResult.IsSuccess)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = [emailResult.Error.Message]
            });

        var userResult = User.Register(emailResult.Value, body.DisplayName);
        if (!userResult.IsSuccess)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["displayName"] = [userResult.Error.Message]
            });

        var user = userResult.Value;
        var createResult = await userManager.CreateAsync(user, body.Password);
        if (!createResult.Succeeded)
            return Results.ValidationProblem(createResult.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));

        return Results.Created($"/api/iam/users/{user.Id.Value}", new { userId = user.Id.Value });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest body, UserManager<User> userManager, ITokenService tokenService,
        IamDbContext db, IOptions<TokenIssuanceOptions> options, TimeProvider timeProvider,
        HttpContext http, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(body.Email ?? string.Empty);
        if (user is null || await userManager.IsLockedOutAsync(user))
            return InvalidCredentials();

        if (!await userManager.CheckPasswordAsync(user, body.Password ?? string.Empty))
        {
            await userManager.AccessFailedAsync(user); // drives lockout after N failures
            return InvalidCredentials();
        }

        await userManager.ResetAccessFailedCountAsync(user);

        // TODO: cached permission/role resolution — later slice. Roles are our own domain aggregate, not Identity roles.
        var tokens = await IssueAsync(user, [], tokenService, db, options.Value, timeProvider, http, ct);
        return Results.Ok(tokens);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest body, ITokenService tokenService, IamDbContext db,
        IOptions<TokenIssuanceOptions> options, TimeProvider timeProvider, HttpContext http, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var hash = tokenService.HashRefreshToken(body.RefreshToken ?? string.Empty);

        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null || !existing.IsActive(now))
            return InvalidCredentials();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == existing.UserId, ct);
        if (user is null)
            return InvalidCredentials();

        // Rotate: revoke the presented token, pointing it at its replacement, then mint a fresh pair.
        var (rawRefresh, refreshHash) = tokenService.CreateRefreshToken();
        existing.Revoke(now, refreshHash);

        var tokens = await IssueAsync(user, [], tokenService, db, options.Value, timeProvider, http, ct,
            rawRefresh, refreshHash);
        return Results.Ok(tokens);
    }

    private static async Task<IResult> LogoutAsync(
        RefreshRequest body, ITokenBlacklist blacklist, ICurrentUser currentUser,
        ClaimsPrincipal principal, IamDbContext db, ITokenService tokenService,
        TimeProvider timeProvider, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        // Revoke the presented refresh token if we still hold an active copy.
        var hash = tokenService.HashRefreshToken(body.RefreshToken ?? string.Empty);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is not null && existing.IsActive(now))
            existing.Revoke(now);
        await db.SaveChangesAsync(ct);

        // Blacklist the current access token so it is rejected before its natural expiry.
        var jti = currentUser.TokenId;
        if (!string.IsNullOrEmpty(jti))
        {
            var exp = long.TryParse(principal.FindFirstValue("exp"), out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : now;
            await blacklist.RevokeAsync(jti, exp, ct);
        }

        return Results.NoContent();
    }

    private static async Task<AuthTokensResponse> IssueAsync(
        User user, IEnumerable<string> roles, ITokenService tokenService, IamDbContext db,
        TokenIssuanceOptions options, TimeProvider timeProvider, HttpContext http, CancellationToken ct,
        string rawRefresh = null, string refreshHash = null)
    {
        var now = timeProvider.GetUtcNow();
        var access = tokenService.CreateAccessToken(user, roles);

        if (rawRefresh is null || refreshHash is null)
            (rawRefresh, refreshHash) = tokenService.CreateRefreshToken();

        var lifetime = TimeSpan.FromDays(options.RefreshTokenDays);
        db.RefreshTokens.Add(RefreshToken.Issue(user.Id, refreshHash, now, lifetime, http.GetClientIpAddress()));
        await db.SaveChangesAsync(ct);

        var expiresIn = (int)(access.ExpiresAtUtc - now).TotalSeconds;
        return new AuthTokensResponse(access.Token, rawRefresh, "Bearer", expiresIn);
    }

    private static IResult InvalidCredentials() =>
        Results.Problem(detail: "Invalid credentials.", statusCode: StatusCodes.Status401Unauthorized);
}

public sealed record RegisterRequest(string Email, string DisplayName, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record AuthTokensResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);
