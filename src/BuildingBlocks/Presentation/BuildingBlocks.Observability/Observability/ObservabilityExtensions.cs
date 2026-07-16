using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using BuildingBlocks.Domain.Observability;

namespace BuildingBlocks.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// OpenTelemetry traces + metrics + logs over OTLP (default http://localhost:4317, overridable via
    /// OTEL_EXPORTER_OTLP_ENDPOINT), all sharing one Resource. The SDK stamps trace_id/span_id on every
    /// LogRecord automatically, so logs correlate with traces with no enricher; attribute keys are
    /// normalized to snake_case on export.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, string serviceName)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
        var environment = builder.Environment.EnvironmentName;

        // Trace head sampling: full traces in Development, a configurable fraction elsewhere (default 20%).
        // ParentBased honors the upstream service's sampled decision, so a distributed trace is kept or dropped
        // coherently end to end; only root spans roll the ratio.
        var traceRatio = builder.Environment.IsDevelopment()
            ? 1.0
            : double.TryParse(builder.Configuration["Observability:TraceSampleRatio"], out var r)
                ? r
                : 0.2;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddServiceResource(serviceName, version, environment))
            .WithTracing(t => t
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(traceRatio)))
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql") // EF Core / Npgsql database spans
                .AddSource("MassTransit") // Kafka rider produce/consume spans
                .AddSource(TelemetryConstants.MessagingActivitySourceName) // consume spans re-parented across the outbox boundary
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation() // HTTP server: rate, errors, duration (RED)
                .AddHttpClientInstrumentation() // outbound HTTP
                .AddRuntimeInstrumentation() // GC, heap, thread pool, exceptions
                .AddProcessInstrumentation() // process CPU + memory (USE)
                .AddMeter("MassTransit")
                .AddMeter("Npgsql")
                .AddMeter(TelemetryConstants.RateLimitingMeterName)
                .AddOtlpExporter());

        // Logs: ILogger → OTLP, same Resource, trace_id/span_id auto-attached, keys snake_cased.
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeScopes = true; // carry BeginScope state (e.g. causationId from the consumer)
            o.IncludeFormattedMessage = true; // render the message body, not just the template
            o.ParseStateValues = true; // turn structured state into attributes the processor can rename
            o.SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddServiceResource(serviceName, version, environment));
            o.AddProcessor(new BaggageLogRecordProcessor()); // correlation_id/causation_id from baggage
            o.AddProcessor(new SnakeCaseLogRecordProcessor()); // normalize all keys to snake_case
            o.AddProcessor(new SensitiveDataLogRecordProcessor()); // then mask sensitive keys' values
            o.AddOtlpExporter();
        });

        return builder;
    }

    private static ResourceBuilder AddServiceResource(
        this ResourceBuilder resource, string serviceName, string version, string environment) =>
        resource
            .AddService(serviceName, serviceVersion: version)
            .AddAttributes(
            [
                new("deployment.environment", environment),
                new("host.name", Environment.MachineName),
                new("process.pid", Environment.ProcessId)
            ]);
}