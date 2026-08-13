using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;
using BuildingBlocks.Application.Telemetry;

namespace BuildingBlocks.Observability;

/// <summary>
/// The Serilog counterpart of <c>BaggageLogRecordProcessor</c>: copies the FAANG-trio identifiers from the ambient
/// <see cref="Activity"/> baggage onto each log event. Keys are already snake_case, so the downstream
/// <see cref="SnakeCaseMaskingEnricher"/> leaves them unchanged. <c>trace_id</c>/<c>span_id</c> are attached by the
/// OTLP sink from the same Activity, so they are not enriched here.
/// </summary>
internal sealed class BaggageEnricher : ILogEventEnricher
{
    private static readonly string[] Keys =
    [
        TelemetryConstants.CorrelationIdKey,
        TelemetryConstants.CausationIdKey,
        TelemetryConstants.UserIdKey,
        TelemetryConstants.ClientIpKey,
        TelemetryConstants.TenantIdKey
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
            return;

        foreach (var key in Keys)
        {
            if (activity.GetBaggageItem(key) is { } value)
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(key, value));
        }
    }
}
