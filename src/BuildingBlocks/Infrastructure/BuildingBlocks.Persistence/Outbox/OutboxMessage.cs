namespace BuildingBlocks.Persistence.Outbox;

// One row per integration event. Column names match the Debezium Outbox Event Router config.
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7(); // = MessageId; router 'id' field
    public string AggregateType { get; init; } = default!; // -> topic suffix (e.g. "platform")
    public string AggregateId { get; init; } = default!; // -> Kafka partition key
    public string EventType { get; init; } = default!; // CLR type name, header
    public int Version { get; init; } = 1;

    public DateTimeOffset OccurredOn { get; init; }

    // The event-driven trio. CorrelationId is a GUID → Debezium emits a CorrelationId header that
    // MassTransit maps natively into ConsumeContext.CorrelationId; CausationId travels as a custom header the
    // consume filter restores; trace continuity travels in TraceParent.
    public string CorrelationId { get; init; } // ties every event in one flow (GUID)
    public string CausationId { get; init; } // id of the message that directly caused this one
    public string TraceParent { get; init; } // W3C traceparent captured at write time -> Kafka header
    public string Payload { get; init; } = default!; // jsonb
}