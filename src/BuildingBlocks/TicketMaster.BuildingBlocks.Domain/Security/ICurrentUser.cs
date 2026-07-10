using YC.Monad;

namespace TicketMaster.BuildingBlocks.Domain.Security;

/// <summary>
/// The authenticated caller, as seen by the application/domain layers — an injectable abstraction over the raw
/// <c>ClaimsPrincipal</c> so handlers, the auditing interceptor, and domain services never reach into
/// <c>HttpContext</c> directly (and stay testable). Scoped: reflects the current request/message. Optional values
/// are <see cref="Option{T}"/> rather than null — absence is explicit.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The <c>sub</c> claim — the stable subject id; None when unauthenticated.</summary>
    Option<string> UserId { get; }

    Option<string> UserName { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    bool HasPermission(string permission);
}
