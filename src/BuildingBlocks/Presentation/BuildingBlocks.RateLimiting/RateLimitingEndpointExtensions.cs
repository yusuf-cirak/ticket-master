using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Opt-in, ASP.NET-idiomatic rate limiting: attach the burst and/or adaptive layers to a single endpoint or a whole
/// route group via <c>Require*RateLimiting()</c> — instead of a global middleware that runs on every request. Only
/// endpoints that opt in are limited; everything else (health, docs, static) is untouched.
/// </summary>
public static class RateLimitingEndpointExtensions
{
    /// <summary>
    /// Per-caller burst limiting: the native <see cref="RateLimitingConstants.Policies.Burst"/> policy plus a fixed
    /// soft-ban that outlives the sliding window (a breaching caller stays 429 for the ban duration).
    /// </summary>
    public static TBuilder RequireBurstRateLimiting<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireRateLimiting(RateLimitingConstants.Policies.Burst);
        builder.AddEndpointFilter<TBuilder, BurstBanEndpointFilter>();
        return builder;
    }

    /// <summary>System-wide adaptive (AIMD) admission control: over the current admission limit ⇒ 503.</summary>
    public static TBuilder RequireAdaptiveRateLimiting<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter<TBuilder, AdaptiveAdmissionEndpointFilter>();
        return builder;
    }

    /// <summary>Both layers at once — per-caller burst shedding first, then the system-wide adaptive gate.</summary>
    public static TBuilder RequireApiRateLimiting<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireBurstRateLimiting();
        builder.RequireAdaptiveRateLimiting();
        return builder;
    }
}
