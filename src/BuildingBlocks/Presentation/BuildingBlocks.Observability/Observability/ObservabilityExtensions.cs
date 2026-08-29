using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;
using BuildingBlocks.Application.Telemetry;

namespace BuildingBlocks.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// OpenTelemetry traces + metrics + logs over OTLP (default http://localhost:4317, overridable via
    /// OTEL_EXPORTER_OTLP_ENDPOINT), all sharing one Resource. Logs use OpenTelemetry by default, or Serilog when
    /// <c>Observability:LoggingBackend</c> is <c>Serilog</c> — both export the same shape (snake_case keys,
    /// correlation_id/causation_id/user_id/client_ip, auto trace_id/span_id, sensitive masking). Traces and metrics are
    /// always OpenTelemetry.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, string serviceName)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
        var environment = builder.Environment.EnvironmentName;

        var options = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
                      ?? new ObservabilityOptions();

        // Trace head sampling: full traces in Development, the configured fraction elsewhere (default 20%).
        // ParentBased honors the upstream service's sampled decision, so a distributed trace is kept or dropped
        // coherently end to end; only root spans roll the ratio.
        var traceRatio = builder.Environment.IsDevelopment() ? 1.0 : options.TraceSampleRatio;

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

        // Logs: OTel-native by default, Serilog when selected. Both end at the same OTLP endpoint.
        if (options.LoggingBackend == LoggingBackend.Serilog)
            builder.AddSerilogLogging(serviceName, version, environment);
        else
            builder.AddOtelLogging(serviceName, version, environment);

        return builder;
    }

    // ILogger → OpenTelemetry LoggerProvider → OTLP, same Resource, trace_id/span_id auto-attached, keys snake_cased.
    private static void AddOtelLogging(this WebApplicationBuilder builder, string serviceName, string version, string environment)
    {
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
    }

    // Same output via Serilog: baggage + snake_case + masking enrichers, exported through the OTLP sink. The sink
    // attaches trace_id/span_id from Activity.Current, so logs still correlate with traces.
    private static void AddSerilogLogging(this WebApplicationBuilder builder, string serviceName, string version, string environment)
    {
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog((_, cfg) => cfg
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.With(new BaggageEnricher())
            .Enrich.With(new SnakeCaseMaskingEnricher())
            .WriteTo.OpenTelemetry(o =>
            {
                o.Endpoint = otlpEndpoint;
                o.Protocol = OtlpProtocol.Grpc;
                o.ResourceAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["service.name"] = serviceName,
                    ["service.version"] = version,
                    ["deployment.environment"] = environment,
                    ["host.name"] = Environment.MachineName,
                    ["process.pid"] = Environment.ProcessId
                };
            }));
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
