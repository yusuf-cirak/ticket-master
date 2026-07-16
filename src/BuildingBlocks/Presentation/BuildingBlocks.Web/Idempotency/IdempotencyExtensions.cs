using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web;

/// <summary>
/// Idempotency keys for unsafe HTTP methods — the commerce-grade guard against double-charges/double-creates when
/// a client retries. Opt an endpoint (or route group) in with <see cref="RequireIdempotency{TBuilder}"/>: it attaches
/// the <see cref="IdempotencyEndpointFilter"/>, so only opted-in endpoints pay the cost — no global middleware.
/// </summary>
public static class IdempotencyExtensions
{
    public static TBuilder RequireIdempotency<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter<TBuilder, IdempotencyEndpointFilter>();
}
