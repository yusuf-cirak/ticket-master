#nullable enable
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Persistence.Outbox;

namespace BuildingBlocks.Messaging.Kafka;

/// <summary>
/// Produces a claimed outbox row to Kafka in the <b>exact same shape</b> Debezium's Outbox Event Router would, so the
/// raw-JSON consumers can't tell the polling relay from the CDC relay: topic <c>tm.{aggregate_type}.events</c>, key =
/// aggregate id, value = the raw JSON payload, and the same identity/trace headers (<see cref="MessageHeaders"/>).
/// Idempotent producer, so a retry after an ambiguous ack doesn't duplicate.
/// </summary>
internal sealed class KafkaOutboxEventPublisher(IProducer<string, string> producer) : IOutboxEventPublisher
{
    public async Task PublishAsync(ClaimedOutboxMessage message, CancellationToken cancellationToken)
    {
        var (topic, kafkaMessage) = BuildMessage(message);
        await producer.ProduceAsync(topic, kafkaMessage, cancellationToken);
    }

    /// <summary>Builds the Debezium-parity topic + message; pure, so the wire format can be asserted without a broker.</summary>
    internal static (string Topic, Message<string, string> Message) BuildMessage(ClaimedOutboxMessage message)
    {
        var headers = new Headers();
        AddHeader(headers, MessageHeaders.MessageId, message.Id.ToString());
        AddHeader(headers, MessageHeaders.EventType, message.EventType);
        AddHeader(headers, MessageHeaders.CorrelationId, message.CorrelationId);
        AddHeader(headers, MessageHeaders.CausationId, message.CausationId);
        AddHeader(headers, MessageHeaders.TraceParent, message.TraceParent);

        var kafkaMessage = new Message<string, string>
        {
            Key = message.AggregateId,
            Value = message.Payload,
            Headers = headers
        };

        return ($"tm.{message.AggregateType}.events", kafkaMessage);
    }

    private static void AddHeader(Headers headers, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            headers.Add(key, Encoding.UTF8.GetBytes(value));
    }
}

public static class KafkaOutboxPublisherExtensions
{
    /// <summary>Registers the shared idempotent Kafka producer + the outbox publisher the polling relay uses.</summary>
    public static IServiceCollection AddKafkaOutboxPublisher(this IServiceCollection services, string bootstrapServers)
    {
        services.AddSingleton<IProducer<string, string>>(_ =>
            new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = Acks.All,          // wait for the full ISR — no acknowledged-then-lost messages
                EnableIdempotence = true, // no duplicates on producer retry
                MessageSendMaxRetries = 10
            }).Build());

        services.AddSingleton<IOutboxEventPublisher, KafkaOutboxEventPublisher>();
        return services;
    }
}
