using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace YC.BuildingBlocks.RateLimiting;

/// <summary>
/// Resolves the rate-limit principal and partition key from the request, following the yc-hardening precedence:
/// authenticated user (<c>sub</c> claim) → API key (<c>X-Api-Key</c>, hashed) → <c>hash(remote IP)</c>.
/// <para>
/// Security: the principal is derived only from values an attacker cannot freely vary — the server-observed IP,
/// not the client-supplied User-Agent (which would let an attacker mint unlimited anonymous buckets). The
/// partition is keyed on the matched <b>route template</b> (not the raw URL path), so <c>/api/items/1</c> and
/// <c>/api/items/2</c> share one bucket and path iteration cannot inflate the effective limit. The privileged
/// <c>internal</c> tier is granted only when the identity was authenticated by a trusted internal scheme AND
/// carries the namespaced tier claim — an external IdP cannot forge it.
/// </para>
/// </summary>
public sealed class PrincipalPartitioner(IOptions<RateLimitingOptions> options)
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string TierClaim = "urn:ticketmaster:tier";
    private const string InternalTierValue = "internal";

    /// <summary>The auth scheme that is allowed to assert the internal tier (e.g. mTLS / internal issuer).</summary>
    private const string InternalAuthScheme = "Internal";


    public (string Id, PrincipalTier Tier) ResolvePrincipal(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(sub))
            return ($"user:{sub}", ResolveTier(context));

        if (context.Request.Headers.TryGetValue(ApiKeyHeader, out var apiKey) && !string.IsNullOrEmpty(apiKey))
            return ($"apiKey:{Hash(apiKey!)}", PrincipalTier.Authenticated);

        // Anonymous: bucket on the server-observed IP only. The User-Agent is attacker-controlled and must not
        // influence the partition (varying it would otherwise mint unlimited buckets and bypass the limit).
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return ($"anon:ip:{Hash(ip)}", PrincipalTier.Anonymous);
    }

    /// <summary>Redis partition key: <c>{prefix}:ratelimit:{policy}:{principal}|{routeScope}</c>.</summary>
    public string PartitionKey(string policyName, string principalId, string routeScope) =>
        $"{Prefix}:ratelimit:{policyName}:{principalId}|{routeScope}";

    /// <summary>The matched route template (never the raw, attacker-controlled path); empty when unmatched.</summary>
    public static string RouteScope(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty;

    public double TierMultiplier(PrincipalTier tier) =>
        options.Value.Tiers.GetValueOrDefault(TierKey(tier), 1.0);

    internal string Prefix => options.Value.KeyPrefix ?? "app";

    internal static string TierKey(PrincipalTier tier) => tier switch
    {
        PrincipalTier.Anonymous => "anon",
        PrincipalTier.Internal => "internal",
        _ => "authenticated"
    };

    // The internal tier (5× limits) is gated on a server-trusted authentication scheme, not just a claim value,
    // so a token from any other issuer cannot self-assert it.
    private static PrincipalTier ResolveTier(HttpContext context)
    {
        var trustedScheme = string.Equals(context.User.Identity?.AuthenticationType, InternalAuthScheme, StringComparison.Ordinal);
        var asserted = context.User.FindFirst(TierClaim)?.Value == InternalTierValue;
        return trustedScheme && asserted ? PrincipalTier.Internal : PrincipalTier.Authenticated;
    }

    // Short, stable, non-reversible partition component — avoids unbounded cardinality from raw IP/API-key.
    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }
}
