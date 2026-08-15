#nullable enable
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BuildingBlocks.Persistence.Outbox;

namespace BuildingBlocks.Messaging.Kafka;

/// <summary>
/// Produces a claimed outbox row to Kafka in the <b>exact same shape</b> Debezium's Outbox Event Router would, so the
/// raw-JSON consumers can't tell the polling relay from the CDC relay: topic <c>tm.{aggregate_type}.events</c>, key =
/// aggregate id, value = the raw JSON payload, and the same identity/trace headers (<see cref="MessageHeaders"/>).
/// Idempotent producer, so a retry after an ambiguous ack doesn't duplicate.
/// </summary>
internal sealed class KafkaOutboxEventPublisher(
    IProducer<string, string> producer,
    ILogger<KafkaOutboxEventPublisher> logger) : IOutboxEventPublisher
{
    public async Task<IReadOnlyList<Guid>> PublishBatchAsync(
        IReadOnlyList<ClaimedOutboxMessage> messages, CancellationToken cancellationToken)
    {
        // Enqueue the whole batch up front — ProduceAsync is a non-blocking enqueue into librdkafka's queue, so the
        // queue now holds every row and librdkafka coalesces them into a few produce requests (linger.ms window)
        // instead of one blocking round-trip per row. Idempotence + key=aggregate_id keep per-aggregate ordering.
        var inflight = new List<(Guid Id, Task Delivery)>(messages.Count);
        foreach (var message in messages)
        {
            var (topic, kafkaMessage) = BuildMessage(message);
            inflight.Add((message.Id, producer.ProduceAsync(topic, kafkaMessage, cancellationToken)));
        }

        return await CollectSucceededAsync(inflight, logger);
    }

    /// <summary>
    /// Awaits each delivery report and returns the ids that were acknowledged. A failed produce is logged and its id
    /// omitted, so the row keeps its lease and is retried later (at-least-once; the consumer inbox dedups duplicates).
    /// </summary>
    internal static async Task<IReadOnlyList<Guid>> CollectSucceededAsync(
        IReadOnlyList<(Guid Id, Task Delivery)> inflight, ILogger logger)
    {
        var published = new List<Guid>(inflight.Count);
        foreach (var (id, delivery) in inflight)
        {
            try
            {
                await delivery;
                published.Add(id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox publish failed for {Id}; it stays leased and is retried later", id);
            }
        }

        return published;
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
                EnableIdempotence = true, // no duplicates on producer retry (also caps in-flight ≤5, preserves order)
                MessageSendMaxRetries = 10,
                LingerMs = 10,            // batch window: lets the pipelined batch coalesce into a few produce requests
                CompressionType = CompressionType.Lz4 // raw-JSON payloads compress well; cuts network + replication bytes
            }).Build());

        services.AddSingleton<IOutboxEventPublisher, KafkaOutboxEventPublisher>();
        return services;
    }
}
