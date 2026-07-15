using Microsoft.Extensions.Logging;

namespace YC.BuildingBlocks.Observability;

/// <summary>
/// Tail log sampling: per request, buffer the chatty low-severity logs and decide at the end whether to keep
/// them. A request that <b>errored</b> (5xx / unhandled exception) flushes its whole buffer — you get the full
/// story, pre-error context included. A healthy request keeps its buffer only <see cref="SampleRate"/> of the
/// time, otherwise drops it. Warning and above are never buffered, so they are always emitted immediately.
/// </summary>
public sealed class LogSamplingOptions
{
    public const string SectionName = "Logging:Sampling";

    /// <summary>Master switch. Sampling is additionally skipped in Development (keep full local logs).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Fraction (0..1) of healthy requests whose buffered logs are kept. Default 20%.</summary>
    public double SampleRate { get; set; } = 0.2;

    /// <summary>Logs at or below this level are buffered; above it they stream immediately. Default Information.</summary>
    public LogLevel BufferBelowLevel { get; set; } = LogLevel.Information;
}
