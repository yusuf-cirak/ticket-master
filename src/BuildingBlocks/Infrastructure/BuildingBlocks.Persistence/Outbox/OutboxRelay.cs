#nullable enable
namespace BuildingBlocks.Persistence.Outbox;

/// <summary>How outbox rows reach Kafka.</summary>
public enum OutboxRelay
{
    /// <summary>Debezium tails the outbox table via logical replication (the app produces nothing). Default.</summary>
    Debezium,

    /// <summary>The app polls the outbox and produces to Kafka itself — for deployments without Debezium/CDC.</summary>
    Polling
}

/// <summary>Outbox relay configuration, bound from the <c>Outbox</c> config section.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public OutboxRelay Relay { get; set; } = OutboxRelay.Debezium;

    /// <summary>How long to wait after an empty poll before looking again (a non-empty poll loops immediately).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Rows claimed per poll.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Claim lease: a claimed-but-unpublished row becomes reclaimable after this (covers a crashed relay).</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>A row claimed for publishing — the subset the publisher needs, materialized outside change tracking.</summary>
public sealed class ClaimedOutboxMessage
{
    public Guid Id { get; init; }
    public string AggregateType { get; init; } = default!;
    public string AggregateId { get; init; } = default!;
    public string EventType { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? TraceParent { get; init; }
}

/// <summary>
/// Publishes a claimed outbox row to the broker. The polling relay calls this <b>outside any database transaction</b>,
/// so an implementation must never assume an ambient DB connection. The Kafka implementation lives in the messaging
/// block and produces the exact same topic/key/value/headers Debezium's outbox router would, so consumers can't tell
/// the two relays apart.
/// </summary>
public interface IOutboxEventPublisher
{
    Task PublishAsync(ClaimedOutboxMessage message, CancellationToken cancellationToken);
}
