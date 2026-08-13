using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;

namespace BuildingBlocks.Web;

/// <summary>
/// Robustness defaults for the host: Kestrel DoS limits, a per-request server-side timeout, graceful shutdown
/// drain, and the standard outbound-HTTP resilience pipeline (timeout + retry + circuit breaker) for every
/// <see cref="HttpClient"/>.
/// </summary>
public static class ResilienceExtensions
{
    private const long MaxRequestBodyBytes = 10 * 1024 * 1024; // 10 MB
    private const int MaxRequestHeadersBytes = 32 * 1024;      // 32 KB
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownDrain = TimeSpan.FromSeconds(10);

    public static WebApplicationBuilder AddResilience(this WebApplicationBuilder builder)
    {
        // Drain in-flight requests + hosted services (Kafka consumers) on SIGTERM before the process exits.
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = ShutdownDrain);

        // DoS limits + don't advertise the server.
        builder.Services.Configure<KestrelServerOptions>(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
            k.Limits.MaxRequestHeadersTotalSize = MaxRequestHeadersBytes;
        });

        // Per-request server-side timeout (runaway handler / slow client) → 504.
        builder.Services.AddRequestTimeouts(o => o.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = RequestTimeout,
            TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
        });

        // Every HttpClient gets the standard resilience pipeline: timeout + retry + circuit breaker.
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        return builder;
    }
}
