namespace BuildingBlocks.Observability;

/// <summary>Which logging backend produces the app's logs. Both export to the same OTLP endpoint with identical
/// resource, snake_case keys, baggage enrichment and sensitive masking — only the pipeline differs.</summary>
public enum LoggingBackend
{
    /// <summary>ILogger → OpenTelemetry LoggerProvider → OTLP (the default).</summary>
    OpenTelemetry,

    /// <summary>ILogger → Serilog → OTLP sink.</summary>
    Serilog
}

/// <summary>Observability tuning, bound from the <c>Observability</c> config section.</summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>Logging pipeline to use. Traces and metrics are always OpenTelemetry regardless.</summary>
    public LoggingBackend LoggingBackend { get; set; } = LoggingBackend.OpenTelemetry;

    /// <summary>Head-sampling ratio for root traces outside Development (Development always samples 1.0).</summary>
    public double TraceSampleRatio { get; set; } = 0.2;
}
