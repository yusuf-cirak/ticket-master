#nullable enable
using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using BuildingBlocks.Persistence.Outbox;
using TicketMaster.Modules.Platform;
using Xunit;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// Exercises the polling outbox relay's claim/lease/complete cycle against the spine's real Postgres (SKIP LOCKED and
/// leasing are Postgres semantics an in-memory provider can't reproduce). The publish step uses a recording/failing
/// fake, so the test asserts the relay mechanics without a broker.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class OutboxPollingTest(SpineFixture fixture)
{
    private OutboxPollingService<PlatformDbContext> Relay(IOutboxEventPublisher publisher) =>
        new(fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            Options.Create(new OutboxOptions { BatchSize = 100, LeaseDuration = TimeSpan.FromMinutes(5) }),
            NullLogger<OutboxPollingService<PlatformDbContext>>.Instance,
            schema: "platform");

    private async Task<Guid> SeedOutboxRowAsync()
    {
        var id = Guid.CreateVersion7();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO platform.outbox
                (id, aggregate_type, aggregate_id, event_type, version, occurred_on, correlation_id, causation_id, trace_parent, payload)
            VALUES ({0}, 'platform', {1}, 'TestEvent', 1, now(), 'corr', 'cause', '00-trace-01', CAST({2} AS jsonb))
            """,
            id, id.ToString(), """{"probe":true}""");
        return id;
    }

    private async Task<(DateTimeOffset? ProcessedOn, DateTimeOffset? LockedUntil)> RowStateAsync(Guid id)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var row = await db.Outbox.AsNoTracking().SingleAsync(o => o.Id == id);
        return (row.ProcessedOn, row.LockedUntil);
    }

    [Fact]
    public async Task Relay_publishes_a_pending_row_then_marks_it_processed_and_does_not_republish()
    {
        var id = await SeedOutboxRowAsync();
        var publisher = new RecordingPublisher();

        await Relay(publisher).RelayBatchAsync(CancellationToken.None);

        publisher.Published.Should().Contain(m => m.Id == id, "the pending row is relayed");
        var afterFirst = await RowStateAsync(id);
        afterFirst.ProcessedOn.Should().NotBeNull("a published row is stamped processed");

        // A second poll must not see it again.
        publisher.Published.Clear();
        await Relay(publisher).RelayBatchAsync(CancellationToken.None);
        publisher.Published.Should().NotContain(m => m.Id == id, "a processed row is never relayed again");
    }

    [Fact]
    public async Task A_failed_publish_leaves_the_row_leased_and_unprocessed_so_it_is_not_double_claimed()
    {
        var id = await SeedOutboxRowAsync();

        // Publishing throws → the row is claimed (leased) but not completed.
        await Relay(new FailingPublisher()).RelayBatchAsync(CancellationToken.None);

        var state = await RowStateAsync(id);
        state.ProcessedOn.Should().BeNull("the publish failed, so it isn't marked processed");
        state.LockedUntil.Should().NotBeNull("it holds a lease");
        state.LockedUntil!.Value.Should().BeAfter(DateTimeOffset.UtcNow,
            "the live lease blocks another poll from re-claiming it until it expires");

        // An immediate re-poll must skip the still-leased row.
        var publisher = new RecordingPublisher();
        await Relay(publisher).RelayBatchAsync(CancellationToken.None);
        publisher.Published.Should().NotContain(m => m.Id == id, "the live lease blocks a double claim");
    }

    private sealed class RecordingPublisher : IOutboxEventPublisher
    {
        public ConcurrentBag<ClaimedOutboxMessage> Published { get; } = new();
        public Task PublishAsync(ClaimedOutboxMessage message, CancellationToken cancellationToken)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingPublisher : IOutboxEventPublisher
    {
        public Task PublishAsync(ClaimedOutboxMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("broker down");
    }
}
