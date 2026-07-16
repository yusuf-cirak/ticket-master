namespace BuildingBlocks.Messaging.Kafka;

/// <summary>
/// Kafka message-header names emitted by the Debezium Outbox Event Router (see
/// infra/debezium/outbox-connector.json) from the outbox columns. The event-driven trio:
/// <list type="bullet">
///   <item><c>MessageId</c>/<c>CorrelationId</c> use MassTransit's exact names, so its raw-JSON consume
///   context populates <see cref="MassTransit.ConsumeContext.MessageId"/> /
///   <see cref="MassTransit.ConsumeContext.CorrelationId"/> natively (values must be GUID-parseable).</item>
///   <item><c>causationId</c> is not a MassTransit concept; the consume filter restores it from this header.</item>
///   <item><c>traceparent</c>/<c>tracestate</c> are the W3C trace headers — MassTransit does NOT read these,
///   so the consume filter re-parents the span from them (trace continuity must never be lost).</item>
/// </list>
/// </summary>
public static class MessageHeaders
{
    // MassTransit identity conventions — mapped natively into ConsumeContext.
    public const string MessageId = "MessageId";
    public const string CorrelationId = "CorrelationId";

    // Domain causation — not a MassTransit concept; restored by the consume filter.
    public const string CausationId = "causationId";

    // W3C trace context — read by the consume filter (MassTransit ignores these).
    public const string TraceParent = "traceparent";
    public const string TraceState = "tracestate";

    // Custom diagnostic header (the CLR event type name).
    public const string EventType = "eventType";
}
