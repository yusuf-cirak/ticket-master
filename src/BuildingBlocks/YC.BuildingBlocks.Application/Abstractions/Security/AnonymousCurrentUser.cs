using YC.BuildingBlocks.Domain.Security;
using YC.Monad;

namespace YC.BuildingBlocks.Application.Abstractions.Security;

/// <summary>
/// Fail-closed default <see cref="ICurrentUser"/> (no identity) so the pipeline always resolves; the host's
/// HttpContext-backed current user overrides it. If the real one is ever missing, sensitive requests see an
/// unauthenticated caller and are rejected rather than silently trusted.
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;
    public Option<string> UserId => Option<string>.None();
    public Option<string> TokenId => Option<string>.None();
    public Option<string> UserName => Option<string>.None();
    public IReadOnlyCollection<string> Roles => [];
    public IReadOnlyCollection<string> Permissions => [];
    public bool HasPermission(string permission) => false;
}
