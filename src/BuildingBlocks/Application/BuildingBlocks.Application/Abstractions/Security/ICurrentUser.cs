
namespace BuildingBlocks.Application.Abstractions.Security;

/// <summary>
/// The authenticated caller, as seen by the application/domain layers — an injectable abstraction over the raw
/// <c>ClaimsPrincipal</c> so handlers, the auditing interceptor, and domain services never reach into
/// <c>HttpContext</c> directly (and stay testable). Scoped: reflects the current request/message. An absent value is
/// <see cref="Option{T}.None"/> (not an empty-string sentinel); <see cref="IsAuthenticated"/> reflects the same.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The <c>sub</c> claim — the stable subject id; <c>None</c> when unauthenticated.</summary>
    Option<string> UserId { get; }

    /// <summary>The <c>jti</c> claim — the current token's id; <c>None</c> when unauthenticated. Used to re-check revocation.</summary>
    Option<string> TokenId { get; }

    /// <summary>The display name (<c>name</c> / <c>preferred_username</c>); <c>None</c> when absent.</summary>
    Option<string> UserName { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    bool HasPermission(string permission);
}
