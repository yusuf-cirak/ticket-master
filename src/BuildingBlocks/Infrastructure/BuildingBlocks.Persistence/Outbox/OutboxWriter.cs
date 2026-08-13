using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Application.Telemetry;
using BuildingBlocks.Application.Messaging;

namespace BuildingBlocks.Persistence.Outbox;

public sealed class OutboxWriter(DbContext dbContext) : IOutboxWriter
{
    public OutboxMessage Enqueue(IntegrationEvent @event, string aggregateType, string aggregateId,
        string correlationId = null, string causationId = null)
    {
        var row = new OutboxMessage
        {
            Id = @event.MessageId,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = @event.GetType().Name,
            Version = 1,
            OccurredOn = @event.OccurredOn,
            // Durable correlation id, captured from the ambient trace baggage (set at the HTTP boundary or by
            // the consume filter). Distinct from the trace id, which travels separately in TraceParent below.
            // It is a GUID, so MassTransit maps the emitted CorrelationId header into ConsumeContext natively.
            CorrelationId = correlationId ?? Activity.Current?.GetBaggageItem(TelemetryConstants.CorrelationIdKey),
            CausationId = causationId,
            // Capture the current trace context so the eventual Kafka consumer can join this trace.
            TraceParent = Activity.Current?.Id,
            Payload = JsonSerializer.Serialize(@event, @event.GetType())
        };
        
        dbContext.Set<OutboxMessage>().Add(row);
        return row;
    }
}
