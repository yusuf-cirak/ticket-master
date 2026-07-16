namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Well-known names for the rate-limiting building block — policy ids, tier keys, principal prefixes, and headers —
/// centralized so endpoints, filters, the partitioner, and configuration all agree on one source of truth.
/// </summary>
public static class RateLimitingConstants
{
    /// <summary>Named rate-limiter policies (the id passed to ASP.NET <c>RequireRateLimiting</c>).</summary>
    public static class Policies
    {
        /// <summary>Per-caller burst (short-window) policy.</summary>
        public const string Burst = "burst";
    }

    /// <summary>Tier keys used in the <c>RateLimiting:Tiers</c> multiplier map.</summary>
    public static class Tiers
    {
        public const string Anonymous = "anon";
        public const string Authenticated = "authenticated";
        public const string Internal = "internal";
    }

    /// <summary>Request header carrying an API key for anonymous-but-keyed callers.</summary>
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>Authentication scheme trusted to assert the privileged internal tier.</summary>
    public const string InternalAuthScheme = "Internal";

    /// <summary>Claim value marking the internal tier.</summary>
    public const string InternalTierValue = "internal";

    /// <summary>Product-neutral default claim type carrying the caller tier; override via <see cref="RateLimitingOptions.TierClaim"/>.</summary>
    public const string DefaultTierClaim = "urn:yc:ratelimit:tier";

    // Principal-id prefixes (kept distinct so buckets never collide across identity kinds).
    internal const string UserPrincipalPrefix = "user:";
    internal const string ApiKeyPrincipalPrefix = "apiKey:";
    internal const string AnonymousPrincipalPrefix = "anon:ip:";

    /// <summary>Key prefix for local (in-memory) burst-ban entries, namespacing them within the shared IMemoryCache.</summary>
    internal const string BanCacheKeyPrefix = "ratelimit:ban:";
}
