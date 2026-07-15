using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace YC.BuildingBlocks.Observability;

/// <summary>
/// Request/response logging via the built-in HTTP logging middleware: properties, an allow-listed set of headers,
/// and (outside Production) the bodies — all combined into one log entry per request and bounded to
/// <see cref="BodyLogLimit"/> bytes. This is the single request-logging mechanism (the per-endpoint policy in
/// <see cref="LoggingPolicyExtensions"/> tunes it: <c>.LogWithoutBody()</c> / <c>.SuppressLogging()</c>).
/// <para>
/// Security: headers are an <b>allow-list</b> — anything not explicitly listed (Authorization, Cookie, Set-Cookie,
/// API keys, …) is redacted, so secrets never reach the logs. The query string is logged via
/// <see cref="SensitiveQueryLogInterceptor"/> with sensitive parameter values redacted (never the raw
/// <c>HttpLoggingFields.RequestQuery</c>). Bodies have only a size cap, not field-level redaction, so they can
/// contain PII/credentials; body capture is therefore <b>off in Production</b> (properties and headers are still
/// logged there). Only text/JSON media is captured.
/// </para>
/// Enabled in every environment by default; turn it off entirely with <c>Logging:HttpDetailed:Enabled=false</c>.
/// </summary>
public static class HttpLoggingExtensions
{
    internal const int BodyLogLimit = 1024;
    private const string EnabledKey = "Logging:HttpDetailed:Enabled";

    // Safe, non-sensitive headers added to the framework's already-safe defaults. Never add Authorization,
    // Cookie, Set-Cookie, Proxy-Authorization, or API-key headers here.
    private static readonly string[] ExtraRequestHeaders = ["X-Correlation-Id", "X-Request-Id", "Origin", "Referer"];
    private static readonly string[] ExtraResponseHeaders = ["X-Correlation-Id", "X-Trace-Id"];

    public static WebApplicationBuilder AddDetailedHttpLogging(this WebApplicationBuilder builder)
    {
        if (!DetailedLoggingEnabled(builder.Configuration))
            return builder;

        // Bodies only outside Production (they can carry PII/credentials and have no field-level redaction).
        var captureBodies = !builder.Environment.IsProduction();

        // The raw query string is NOT logged via HttpLoggingFields.RequestQuery — it can carry secrets (OAuth
        // codes/tokens, signed-URL signatures, API keys). SensitiveQueryLogInterceptor logs a redacted query instead.
        builder.Services.AddHttpLoggingInterceptor<SensitiveQueryLogInterceptor>();

        builder.Services.AddHttpLogging(o =>
        {
            o.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders
                              | HttpLoggingFields.ResponsePropertiesAndHeaders
                              | HttpLoggingFields.Duration;
            if (captureBodies)
            {
                o.LoggingFields |= HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseBody;
            }

            o.CombineLogs = true; // one entry per request: request + response together
            o.RequestBodyLogLimit = BodyLogLimit; // ≤ 1k
            o.ResponseBodyLogLimit = BodyLogLimit;

            foreach (var h in ExtraRequestHeaders) o.RequestHeaders.Add(h);
            foreach (var h in ExtraResponseHeaders) o.ResponseHeaders.Add(h);

            // Capture only text bodies; binary/streamed media is skipped entirely.
            o.MediaTypeOptions.Clear();
            o.MediaTypeOptions.AddText("application/json");
            o.MediaTypeOptions.AddText("text/plain");
        });

        return builder;
    }

    public static WebApplication UseDetailedHttpLogging(this WebApplication app)
    {
        if (DetailedLoggingEnabled(app.Configuration))
        {
            app.UseHttpLogging();
        }

        return app;
    }

    private static bool DetailedLoggingEnabled(IConfiguration configuration) =>
        ResolveEnabled(configuration[EnabledKey]);

    // On in every environment by default; off only when explicitly configured "false".
    internal static bool ResolveEnabled(string configuredValue) =>
        !bool.TryParse(configuredValue, out var enabled) || enabled;
}