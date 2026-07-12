using TicketMaster.Modules.Iam.Application.Features.Authentication;
using TicketMaster.Modules.Iam.Domain;

namespace TicketMaster.Modules.Iam.Application.Abstractions;

/// <summary>
/// Mints an access/refresh token pair for a user and persists the refresh token. Callers either let it create a fresh
/// refresh token (login) or hand it a pre-rotated one (<paramref name="rawRefresh"/>/<paramref name="refreshHash"/>
/// on refresh). Keeps the JWT/DB plumbing out of the command handlers.
/// </summary>
public interface IAuthTokenIssuer
{
    Task<AuthTokensResponse> IssueAsync(User user, string ip, CancellationToken ct, string rawRefresh = null, string refreshHash = null);
}
