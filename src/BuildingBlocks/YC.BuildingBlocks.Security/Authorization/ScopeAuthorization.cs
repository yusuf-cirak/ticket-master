using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace YC.BuildingBlocks.Security.Authorization;

/// <summary>Requires the principal to carry at least one of the listed OAuth scopes.</summary>
public sealed class ScopeRequirement(IReadOnlyCollection<string> scopes) : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> Scopes { get; } = scopes;
}

/// <summary>
/// Grants a <see cref="ScopeRequirement"/> when the token's scopes intersect the required set. Scopes are read from
/// both conventions: a single space-delimited <c>scope</c> claim (OAuth standard) and repeated <c>scp</c> claims.
/// </summary>
internal sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        var granted = GrantedScopes(context.User);
        if (requirement.Scopes.Any(granted.Contains))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }

    private static HashSet<string> GrantedScopes(ClaimsPrincipal user)
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in user.FindAll("scope").Concat(user.FindAll("scp")))
            foreach (var scope in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                scopes.Add(scope);

        return scopes;
    }
}

public static class ScopeAuthorizationExtensions
{
    /// <summary>
    /// Endpoint opt-in: require an authenticated user holding at least one of <paramref name="scopes"/>. Follows the
    /// same convention as <c>.RequireIdempotency()</c> / <c>.SuppressLogging()</c>. For roles, use the native
    /// <c>.RequireAuthorization(p =&gt; p.RequireRole(...))</c> (the role claim is mapped).
    /// </summary>
    public static TBuilder RequireScope<TBuilder>(this TBuilder builder, params string[] scopes)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new ScopeRequirement(scopes))
            .Build());
}
