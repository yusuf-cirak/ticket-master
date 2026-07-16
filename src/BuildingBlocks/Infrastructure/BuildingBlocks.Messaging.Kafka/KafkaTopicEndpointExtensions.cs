using Confluent.Kafka;
using MassTransit;

namespace BuildingBlocks.Messaging.Kafka;

public static class KafkaTopicEndpointExtensions
{
    /// <summary>
    /// Standard wiring for every TicketMaster Kafka topic endpoint — so the cross-cutting concerns (above
    /// all the message-context filter that propagates trace/correlation) are applied automatically and the
    /// host never repeats them per endpoint:
    ///   • <see cref="AutoOffsetReset.Earliest"/> (don't skip events produced while absent),
    ///   • raw-JSON deserialization (Debezium emits plain JSON, no MassTransit envelope; identity headers
    ///     map natively into ConsumeContext),
    ///   • <see cref="MessageContextFilter{T}"/> (re-parent the trace span + correlation_id onto logs),
    ///   • a small retry for transient consume faults.
    /// </summary>
    public static void ApplyDefaults(
        this IKafkaTopicReceiveEndpointConfigurator endpoint,
        IRiderRegistrationContext context)
    {
        endpoint.AutoOffsetReset = AutoOffsetReset.Earliest;
        endpoint.CreateIfMissing();
        endpoint.UseRawJsonDeserializer();
        endpoint.UseConsumeFilter(typeof(MessageContextFilter<>), context);
        endpoint.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(2)));
    }
}
