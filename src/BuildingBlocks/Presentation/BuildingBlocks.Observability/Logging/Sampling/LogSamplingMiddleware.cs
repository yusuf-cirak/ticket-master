using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Observability;

/// <summary>
/// End-of-request flush decision for <see cref="LogSamplingOptions"/>. Wraps the pipeline so that an errored
/// request flushes its whole buffered log story, while a healthy request keeps it only at the sampled rate.
/// The buffering itself lives in the logging pipeline (AddPerIncomingRequestBuffer); this middleware only picks
/// flush vs. drop. Buffered records that are never flushed are discarded when the request ends.
/// </summary>
internal sealed class LogSamplingMiddleware(RequestDelegate next, IOptionsMonitor<LogSamplingOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var buffer = context.RequestServices.GetService<PerRequestLogBuffer>();

        try
        {
            await next(context);
        }
        catch
        {
            buffer?.Flush(); // unhandled exception → keep the full request story
            throw;
        }

        if (ShouldFlush(context.Response.StatusCode, options.CurrentValue.SampleRate, Random.Shared.NextDouble()))
        {
            buffer?.Flush();
        }
    }

    /// <summary>Error response → always flush; otherwise keep a healthy request only at the sampled rate.</summary>
    internal static bool ShouldFlush(int statusCode, double sampleRate, double roll) =>
        statusCode >= 500 || roll < sampleRate;
}