
namespace TicketMaster.Modules.Iam.Application.Features.Authentication;

/// <summary>The token pair returned by login/refresh: a signed access token plus its opaque refresh token.</summary>
public sealed record AuthTokensResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);

/// <summary>Shared credential failure. One generic 401 for every path so account existence and lockout state never leak.</summary>
internal static class AuthErrors
{
    public static readonly Error InvalidCredentials = Error.Create("auth.invalid_credentials", "Invalid credentials.", 401);
}
