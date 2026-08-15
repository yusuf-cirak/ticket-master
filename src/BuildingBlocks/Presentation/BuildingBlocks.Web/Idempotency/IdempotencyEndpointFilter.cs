using System.Buffers.Binary;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web;

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

    public async ValueTask<object> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
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

        // Scope the key by caller so one principal's stored response can never be replayed to another. The
        // idempotency key is client-chosen, so an unscoped key lets a caller reuse a victim's key and read back the
        // victim's cached response (cross-user disclosure). Authenticated → the "sub" principal; anonymous → the
        // per-source IP (never crosses distinct callers; a shared-NAT collision would additionally require the same
        // client-chosen key, which is negligible).
        var scope = http.User.FindFirstValue("sub")
                    ?? http.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";
        var cacheKey = $"idempotency:{scope}:{http.Request.Method}:{http.Request.Path}:{key}";

        if (await cache.GetAsync(cacheKey, http.RequestAborted) is { } cached)
            return new ReplayedResult(cached);

        return await CaptureAndStore(context, next, cache, cacheKey);
    }

    private static async Task<object> CaptureAndStore(
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
            buffer.TryGetBuffer(out var body); // MemoryStream's backing array — no extra copy
            await cache.SetAsync(
                cacheKey,
                Encode(http.Response.StatusCode, http.Response.ContentType, body),
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

    // Binary cache blob: [statusCode i32][contentTypeLen i32][contentType utf8][body]. Replaces JSON-wrapping a
    // record whose byte[] body was base64-encoded (~33% larger + reflection serialize) on the idempotent-write path.
    private static byte[] Encode(int statusCode, string contentType, ArraySegment<byte> body)
    {
        var contentTypeBytes = Encoding.UTF8.GetBytes(contentType ?? string.Empty);
        var blob = new byte[8 + contentTypeBytes.Length + body.Count];
        BinaryPrimitives.WriteInt32LittleEndian(blob, statusCode);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4), contentTypeBytes.Length);
        contentTypeBytes.CopyTo(blob.AsSpan(8));
        body.AsSpan().CopyTo(blob.AsSpan(8 + contentTypeBytes.Length));
        return blob;
    }

    /// <summary>Writes a previously-stored response (see <see cref="Encode"/>), tagged with the replay header.</summary>
    private sealed class ReplayedResult(byte[] blob) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var statusCode = BinaryPrimitives.ReadInt32LittleEndian(blob);
            var contentTypeLength = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(4));

            var response = httpContext.Response;
            response.StatusCode = statusCode;
            if (contentTypeLength > 0)
                response.ContentType = Encoding.UTF8.GetString(blob, 8, contentTypeLength);
            response.Headers[ReplayedHeader] = "true";
            await response.Body.WriteAsync(blob.AsMemory(8 + contentTypeLength), httpContext.RequestAborted);
        }
    }
}
