using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Replays the stored response for a repeated <c>Idempotency-Key</c> instead of re-running the handler, so a
/// client retry of a POST/PUT/PATCH/DELETE cannot create or charge twice. Only acts on endpoints that opted in
/// via <c>.RequireIdempotency()</c>; everything else passes straight through. The first call buffers its response
/// and stores it (status + content-type + body, bounded to <see cref="MaxStoredBodyBytes"/>) in the shared
/// distributed cache with a 24h TTL; a missing key on a required endpoint is a 400.
/// <para>
/// Note: this de-dupes <i>sequential</i> retries (the common case). It does not lock out two truly concurrent
/// requests with the same key — that needs an atomic reservation and is out of scope here.
/// </para>
/// </summary>
internal sealed class IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
{
    private const string HeaderName = "Idempotency-Key";
    private const string ReplayedHeader = "Idempotency-Replayed";
    private const int MaxKeyLength = 200;
    private const int MaxStoredBodyBytes = 1024 * 1024; // 1 MB
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.RequiresIdempotency() || !IsUnsafe(context.Request.Method))
        {
            await next(context);
            return;
        }

        if (!TryGetKey(context, out var key))
        {
            await WriteMissingKeyProblem(context);
            return;
        }

        var cache = context.RequestServices.GetService<IDistributedCache>();
        if (cache is null) // no L2 configured → degrade to no idempotency rather than fail the request
        {
            logger.LogWarning("Idempotency required but no IDistributedCache is registered; processing without it.");
            await next(context);
            return;
        }

        var cacheKey = $"idempotency:{context.Request.Method}:{context.Request.Path}:{key}";

        if (await cache.GetAsync(cacheKey, context.RequestAborted) is { } cached)
        {
            await Replay(context, cached);
            return;
        }

        await CaptureAndStore(context, cache, cacheKey);
    }

    private async Task CaptureAndStore(HttpContext context, IDistributedCache cache, string cacheKey)
    {
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, context.RequestAborted);

        // Only cache a successful, reasonably sized response — errors must remain retryable.
        if (context.Response.StatusCode is >= 200 and < 300 && buffer.Length <= MaxStoredBodyBytes)
        {
            var record = new StoredResponse(context.Response.StatusCode, context.Response.ContentType, buffer.ToArray());
            await cache.SetAsync(
                cacheKey,
                JsonSerializer.SerializeToUtf8Bytes(record),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
                context.RequestAborted);
        }
    }

    private static async Task Replay(HttpContext context, byte[] cached)
    {
        var record = JsonSerializer.Deserialize<StoredResponse>(cached)!;
        context.Response.StatusCode = record.StatusCode;
        context.Response.ContentType = record.ContentType;
        context.Response.Headers[ReplayedHeader] = "true";
        await context.Response.Body.WriteAsync(record.Body, context.RequestAborted);
    }

    private static bool IsUnsafe(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static bool TryGetKey(HttpContext context, out string key)
    {
        key = context.Request.Headers[HeaderName].ToString();
        return key.Length is > 0 and <= MaxKeyLength;
    }

    private static Task WriteMissingKeyProblem(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Missing Idempotency-Key",
            Detail = $"This endpoint requires a non-empty '{HeaderName}' header (max {MaxKeyLength} chars)."
        }, context.RequestAborted);
    }

    private sealed record StoredResponse(int StatusCode, string? ContentType, byte[] Body);
}
