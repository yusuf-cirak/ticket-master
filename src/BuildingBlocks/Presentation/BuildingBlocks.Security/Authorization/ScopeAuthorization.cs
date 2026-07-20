using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace BuildingBlocks.Security.Authorization;

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
        if (HasAnyRequiredScope(context.User, requirement.Scopes))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }

    // Runs on every authorized request. Tokenizes the space-delimited scope claim(s) as spans and compares each token
    // to the required set — no HashSet and no per-scope substrings, ordinal like before, and stops at the first match.
    private static bool HasAnyRequiredScope(ClaimsPrincipal user, IReadOnlyCollection<string> required)
    {
        foreach (var claim in user.FindAll("scope").Concat(user.FindAll("scp")))
        {
            var value = claim.Value.AsSpan();
            foreach (var range in value.Split(' '))
            {
                var token = value[range].Trim();
                if (token.IsEmpty)
                    continue;

                foreach (var req in required)
                    if (token.SequenceEqual(req))
                        return true;
            }
        }

        return false;
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
