#nullable enable
using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Opt-in endpoint filter (attached by <see cref="RateLimitingEndpointExtensions.RequireAdaptiveRateLimiting{TBuilder}"/>)
/// enforcing the system-wide AIMD admission limit: over the current limit ⇒ 503. Also times the handler and records
/// latency + status into the load signal, so the adaptive controller is fed by exactly the traffic it governs (no
/// global middleware needed).
/// </summary>
internal sealed class AdaptiveAdmissionEndpointFilter(IAdmissionGate gate, RequestMetricsCollector metrics) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!gate.TryEnter(out var lease))
        {
            await RateLimitingExtensions.Write503Async(http, http.RequestAborted);
            return Results.Empty;
        }

        var start = Stopwatch.GetTimestamp();
        var status = StatusCodes.Status200OK;
        try
        {
            using (lease)
            {
                var result = await next(context);
                status = http.Response.StatusCode; // best-effort (a deferred IResult may not have executed yet)
                return result;
            }
        }
        catch
        {
            status = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            metrics.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, status);
        }
    }
}
