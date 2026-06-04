using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TicketMaster.ServiceDefaults;

/// <summary>Marks an endpoint as requiring a client-supplied <c>Idempotency-Key</c> for safe retries.</summary>
internal sealed class RequireIdempotencyMetadata
{
    public static readonly RequireIdempotencyMetadata Instance = new();
}

/// <summary>
/// Idempotency keys for unsafe HTTP methods — the commerce-grade guard against double-charges/double-creates when
/// a client retries. Opt an endpoint in with <see cref="RequireIdempotency{TBuilder}"/>; the first request with a
/// given <c>Idempotency-Key</c> runs and its response is cached (Redis L2), and any retry with the same key
/// replays that stored response instead of executing the handler again.
/// </summary>
public static class IdempotencyExtensions
{
    public static TBuilder RequireIdempotency<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(RequireIdempotencyMetadata.Instance);

    public static WebApplication UseIdempotency(this WebApplication app)
    {
        app.UseMiddleware<IdempotencyMiddleware>();
        return app;
    }

    internal static bool RequiresIdempotency(this HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<RequireIdempotencyMetadata>() is not null;
}
