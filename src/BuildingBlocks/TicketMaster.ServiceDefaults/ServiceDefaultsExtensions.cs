using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Aspire-style service defaults: one call wires the cross-cutting host concerns (UTC, OpenTelemetry
/// traces/metrics/logs, Kubernetes health checks) so each host stays thin and consistent. Correlation
/// is the auto-generated trace_id — no separate id, package, or middleware to manage.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        // Run the process in UTC regardless of the host's timezone, before any date/time API is touched.
        Environment.SetEnvironmentVariable("TZ", "UTC");
        TimeZoneInfo.ClearCachedData();

        var serviceName = builder.Configuration["ServiceName"] ?? builder.Environment.ApplicationName;

        builder.AddObservability(serviceName); // traces + metrics + logs (OTLP), snake_case keys
        builder.AddLogSampling(); // tail sampling: buffer Info, flush on error / sampled rate
        builder.AddGlobalExceptionHandling(); // RFC 9457 problem+json for unhandled exceptions
        builder.AddDetailedHttpLogging(); // opt-in body/header capture (non-prod default), 1k-bounded
        builder.AddResilience(); // Kestrel limits, request timeout, outbound resilience, shutdown
        builder.AddCorsPolicy(); // CORS from Cors:AllowedOrigins (deny by default)
        builder.AddCompression(); // response compression + request decompression
        builder.Services.AddJsonDefaults(); // one JSON contract: depth cap, string enums, reject unmapped members
        builder.Services.AddOutputCache();
        builder.Services.AddApiVersioningDefaults();
        builder.Services.AddFeatureFlags();
        builder.AddForwardedHeaders(); // trust edge proxy X-Forwarded-* → real client IP + scheme
        builder.AddJwtAuthentication(); // JWT bearer authN + secure-by-default authZ + scope handler
        builder.AddCurrentUser(); // ICurrentUser over the ambient principal (application/domain-facing)
        builder.AddAuditLogging(); // IAuditLogger → structured audit trail on the OTel pipeline
        builder.AddDefaultHealthChecks();

        return builder;
    }

    extension(WebApplication app)
    {
        /// <summary>Pipeline defaults: establish + echo the correlation id (X-Correlation-ID). Call first.</summary>
        public WebApplication UseServiceDefaults()
        {
            // Forwarded headers first: every downstream component (logging, rate limiter, auth) sees the real
            // client IP and request scheme, not the edge proxy's.
            app.UseForwardedHeaders();

            // Security headers + compression are outermost so they apply to every response (including errors).
            app.UseSecurityHeaders(SecurityHeaders.Default);
            // Decompress the request body up front, before anything reads it (request logging, model binding).
            app.UseRequestDecompression();
            
            app.UseResponseCompression();
            
            // Log sampling wraps the request so its buffered logs flush before the sampling decision; the
            // exception handler wraps the endpoints; correlation runs closest to them.
            app.UseLogSampling(); // flush/drop each request's buffered logs
            app.UseExceptionHandler(); // escaping exceptions → RFC 9457 problem+json
            app.UseMiddleware<CorrelationMiddleware>();

            // Routing first, so per-endpoint policy applies: request timeouts, CORS, output cache and HTTP
            // logging (.LogWithoutBody/.SuppressLogging) all act on the matched endpoint.
            app.UseRouting();
            app.UseRequestTimeouts();
            app.UseCors();

            // Auth after routing/CORS, before the rate limiter (Program.cs) which reads the principal's claims.
            // Enrichment runs right after authentication so user_id/client_ip reach the request log and every line.
            app.UseAuthentication();
            app.UseMiddleware<UserContextMiddleware>();
            app.UseAuthorization();

            app.UseOutputCache();
            app.UseDetailedHttpLogging(); // request/response logging (properties, headers, body)
            // Idempotency is now a per-endpoint filter (.RequireIdempotency()) — no global middleware here.

            return app;
        }

        /// <summary>Maps the default health endpoints (/health/liveness, /health/readiness, /health).</summary>
        public WebApplication MapDefaultEndpoints()
        {
            app.MapDefaultHealthEndpoints();
            return app;
        }
    }
}