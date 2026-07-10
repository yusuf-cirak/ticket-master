namespace TicketMaster.BuildingBlocks.Domain.Security;

/// <summary>
/// The authenticated caller, as seen by the application/domain layers — an injectable abstraction over the raw
/// <c>ClaimsPrincipal</c> so handlers, the auditing interceptor, and domain services never reach into
/// <c>HttpContext</c> directly (and stay testable). Scoped: reflects the current request/message. Absent values are
/// the empty string (never null); use <see cref="IsAuthenticated"/> to distinguish.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The <c>sub</c> claim — the stable subject id; empty when unauthenticated.</summary>
    string UserId { get; }

    /// <summary>The <c>jti</c> claim — the current token's id; empty when unauthenticated. Used to re-check revocation.</summary>
    string TokenId { get; }

    string UserName { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    bool HasPermission(string permission);
}
