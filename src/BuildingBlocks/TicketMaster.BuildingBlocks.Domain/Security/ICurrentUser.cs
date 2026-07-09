namespace TicketMaster.BuildingBlocks.Domain.Security;

/// <summary>
/// The authenticated caller, as seen by the application/domain layers — an injectable abstraction over the raw
/// <c>ClaimsPrincipal</c> so handlers, the auditing interceptor, and domain services never reach into
/// <c>HttpContext</c> directly (and stay testable). Scoped: reflects the current request/message.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The <c>sub</c> claim — the stable subject id, or null when unauthenticated.</summary>
    string? UserId { get; }

    string? UserName { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    bool HasPermission(string permission);
}
