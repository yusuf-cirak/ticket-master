using YC.BuildingBlocks.Domain.Security;

namespace YC.BuildingBlocks.Application.Abstractions.Security;

/// <summary>
/// Fail-closed default <see cref="ICurrentUser"/> (no identity) so the pipeline always resolves; the host's
/// HttpContext-backed current user overrides it. If the real one is ever missing, sensitive requests see an
/// unauthenticated caller and are rejected rather than silently trusted.
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;
    public string UserId => string.Empty;
    public string TokenId => string.Empty;
    public string UserName => string.Empty;
    public IReadOnlyCollection<string> Roles => [];
    public IReadOnlyCollection<string> Permissions => [];
    public bool HasPermission(string permission) => false;
}
