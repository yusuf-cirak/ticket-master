#nullable enable
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Opt-in endpoint filter (attached by <see cref="RateLimitingEndpointExtensions.RequireBurstRateLimiting{TBuilder}"/>)
/// that rejects a caller who is inside the fixed soft-ban window with 429 + <c>Retry-After</c> — even after the sliding
/// window has since reset. The ban is armed by the rate limiter's <c>OnRejected</c> when the burst window is breached.
/// </summary>
internal sealed class BurstBanEndpointFilter(IBurstBanStore store) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var scope = RateLimitingExtensions.BurstBanScope(http);

        if (await store.GetRemainingBanAsync(scope, http.RequestAborted) is { } ban)
        {
            await RateLimitingExtensions.Write429Async(http, ban, http.RequestAborted);
            return Results.Empty;
        }

        return await next(context);
    }
}
