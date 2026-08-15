#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using BuildingBlocks.Messaging.Kafka;
using BuildingBlocks.Persistence.Outbox;
using Xunit;

namespace TicketMaster.UnitTests.Messaging;

public class KafkaOutboxPublisherTests
{
    [Fact]
    public void BuildMessage_matches_the_debezium_outbox_router_wire_format()
    {
        var claimed = new ClaimedOutboxMessage
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            AggregateType = "platform",
            AggregateId = "agg-1",
            EventType = "PingIntegrationEvent",
            Payload = """{"ping":1}""",
            CorrelationId = "corr",
            CausationId = "cause",
            TraceParent = "00-trace-01"
        };

        var (topic, message) = KafkaOutboxEventPublisher.BuildMessage(claimed);

        topic.Should().Be("tm.platform.events", "the topic mirrors Debezium's route.topic.replacement tm.${aggregate_type}.events");
        message.Key.Should().Be("agg-1");
        message.Value.Should().Be("""{"ping":1}""", "the raw JSON payload is the value, as the raw-JSON consumers expect");

        Header(message, MessageHeaders.MessageId).Should().Be(claimed.Id.ToString());
        Header(message, MessageHeaders.EventType).Should().Be("PingIntegrationEvent");
        Header(message, MessageHeaders.CorrelationId).Should().Be("corr");
        Header(message, MessageHeaders.CausationId).Should().Be("cause");
        Header(message, MessageHeaders.TraceParent).Should().Be("00-trace-01");
    }

    [Fact]
    public void BuildMessage_omits_headers_for_absent_optional_fields()
    {
        var claimed = new ClaimedOutboxMessage
        {
            Id = Guid.NewGuid(), AggregateType = "events", AggregateId = "e1",
            EventType = "X", Payload = "{}", CorrelationId = null, CausationId = null, TraceParent = null
        };

        var (_, message) = KafkaOutboxEventPublisher.BuildMessage(claimed);

        message.Headers.TryGetLastBytes(MessageHeaders.CorrelationId, out _).Should().BeFalse();
        message.Headers.TryGetLastBytes(MessageHeaders.TraceParent, out _).Should().BeFalse();
    }

    [Fact]
    public async Task CollectSucceeded_returns_only_acknowledged_ids_so_failed_rows_stay_leased()
    {
        Guid g1 = Guid.NewGuid(), g2 = Guid.NewGuid(), g3 = Guid.NewGuid();
        var inflight = new (Guid, Task)[]
        {
            (g1, Task.CompletedTask),
            (g2, Task.FromException(new Exception("broker unavailable"))), // this row's produce failed
            (g3, Task.CompletedTask)
        };

        var published = await KafkaOutboxEventPublisher.CollectSucceededAsync(inflight, NullLogger.Instance);

        published.Should().Equal(g1, g3); // g2 omitted → not marked processed → retried after its lease lapses
    }

    private static string Header(Message<string, string> message, string key) =>
        Encoding.UTF8.GetString(message.Headers.GetLastBytes(key));
}
