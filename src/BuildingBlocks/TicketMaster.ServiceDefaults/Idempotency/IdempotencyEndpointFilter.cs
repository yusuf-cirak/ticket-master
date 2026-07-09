using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Per-endpoint idempotency as an <see cref="IEndpointFilter"/> (opt in with <c>.RequireIdempotency()</c>) — no
/// global middleware scanning every request. A repeated <c>Idempotency-Key</c> on an unsafe method replays the
/// stored response instead of re-running the handler, so a client retry of a POST/PUT/PATCH/DELETE cannot create
/// or charge twice. The first call's response is executed into a buffer, streamed to the client, and stored
/// (status + content-type + body, bounded) in the shared distributed cache with a 24h TTL.
/// <para>
/// De-dupes <i>sequential</i> retries (the common case); it does not lock out two truly concurrent requests with
/// the same key — that needs an atomic reservation and is out of scope here.
/// </para>
/// </summary>
internal sealed class IdempotencyEndpointFilter(ILogger<IdempotencyEndpointFilter> logger) : IEndpointFilter
{
    private const string HeaderName = "Idempotency-Key";
    private const string ReplayedHeader = "Idempotency-Replayed";
    private const int MaxKeyLength = 200;
    private const int MaxStoredBodyBytes = 1024 * 1024; // 1 MB
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!IsUnsafe(http.Request.Method))
            return await next(context);

        if (!TryGetKey(http, out var key))
            return TypedResults.Problem(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing Idempotency-Key",
                Detail = $"This endpoint requires a non-empty '{HeaderName}' header (max {MaxKeyLength} chars)."
            });

        var cache = http.RequestServices.GetService<IDistributedCache>();
        if (cache is null) // no L2 configured → degrade to no idempotency rather than fail the request
        {
            logger.LogWarning("Idempotency required but no IDistributedCache is registered; processing without it.");
            return await next(context);
        }

        var cacheKey = $"idempotency:{http.Request.Method}:{http.Request.Path}:{key}";

        if (await cache.GetAsync(cacheKey, http.RequestAborted) is { } cached)
            return new ReplayedResult(JsonSerializer.Deserialize<StoredResponse>(cached)!);

        return await CaptureAndStore(context, next, cache, cacheKey);
    }

    private static async Task<object?> CaptureAndStore(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next, IDistributedCache cache, string cacheKey)
    {
        var http = context.HttpContext;
        var originalBody = http.Response.Body;
        using var buffer = new MemoryStream();
        http.Response.Body = buffer;

        try
        {
            // Execute the endpoint's result into the buffered body so we capture the exact bytes.
            var result = await next(context);
            var toExecute = result as IResult ?? Results.Json(result);
            await toExecute.ExecuteAsync(http);
        }
        finally
        {
            http.Response.Body = originalBody;
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, http.RequestAborted);

        // Only cache a successful, reasonably sized response — errors must remain retryable.
        if (http.Response.StatusCode is >= 200 and < 300 && buffer.Length <= MaxStoredBodyBytes)
        {
            var record = new StoredResponse(http.Response.StatusCode, http.Response.ContentType, buffer.ToArray());
            await cache.SetAsync(
                cacheKey,
                JsonSerializer.SerializeToUtf8Bytes(record),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
                http.RequestAborted);
        }

        // The response has already been executed and written; nothing more for the framework to do.
        return Results.Empty;
    }

    private static bool IsUnsafe(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static bool TryGetKey(HttpContext context, out string key)
    {
        key = context.Request.Headers[HeaderName].ToString();
        return key.Length is > 0 and <= MaxKeyLength;
    }

    private sealed record StoredResponse(int StatusCode, string? ContentType, byte[] Body);

    /// <summary>Writes a previously-stored response, tagged with the replay header.</summary>
    private sealed class ReplayedResult(StoredResponse record) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = record.StatusCode;
            httpContext.Response.ContentType = record.ContentType;
            httpContext.Response.Headers[ReplayedHeader] = "true";
            await httpContext.Response.Body.WriteAsync(record.Body, httpContext.RequestAborted);
        }
    }
}
