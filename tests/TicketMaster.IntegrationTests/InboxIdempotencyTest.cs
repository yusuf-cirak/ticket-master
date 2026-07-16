using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Persistence.Inbox;
using BuildingBlocks.Persistence.Outbox;
using TicketMaster.Modules.Platform;
using Xunit;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// Proves the inbox idempotency guarantee against a real Postgres unique constraint (an in-memory
/// provider cannot enforce it). Under concurrent at-least-once duplicate delivery, exactly one
/// delivery must commit — the inbox marker and the work's state change persist exactly once and
/// together — and the losing deliveries must resolve gracefully (false, no exception), never a
/// poison-message loop. Uses the shared spine's already-migrated Postgres.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class InboxIdempotencyTest(SpineFixture fixture)
{
    [Fact]
    public async Task ConcurrentDuplicateDelivery_CommitsExactlyOnce_AndNeverThrows()
    {
        var messageId = Guid.CreateVersion7();
        const string consumer = "platform.ping";
        const int parallelism = 20;

        // Each "delivery" is an independent DbContext/scope, mirroring concurrent consumer invocations.
        // The work mutates the same DbContext (enqueues an outbox row) so we can assert the work's
        // state change is committed atomically with the inbox marker — exactly once.
        var deliveries = Enumerable.Range(0, parallelism).Select(async _ =>
        {
            await using var ctx = NewContext();
            var store = new InboxStore(ctx);
            return await store.TryConsumeAsync(
                messageId,
                consumer,
                _ =>
                {
                    ctx.Set<OutboxMessage>().Add(new OutboxMessage
                    {
                        Id = Guid.CreateVersion7(),
                        AggregateType = "test",
                        AggregateId = messageId.ToString(),
                        EventType = "WorkMarker",
                        Version = 1,
                        OccurredOn = DateTimeOffset.UtcNow,
                        Payload = "{}"
                    });
                    return Task.CompletedTask;
                },
                CancellationToken.None);
        }).ToArray();

        var results = await Task.WhenAll(deliveries); // never throws — losers return false, not exceptions

        results.Count(won => won).Should().Be(1, "exactly one concurrent delivery may win the unique key");

        await using var verify = NewContext();
        var inboxRows = await verify.Set<InboxMessage>()
            .CountAsync(m => m.MessageId == messageId && m.Consumer == consumer);
        var workRows = await verify.Set<OutboxMessage>()
            .CountAsync(o => o.AggregateId == messageId.ToString());

        inboxRows.Should().Be(1, "the inbox marker is written exactly once");
        workRows.Should().Be(1, "only the winning delivery's work persists; losers are rolled back");
    }

    private PlatformDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(fixture.PostgresConnectionString)
            // Must match the production model (PlatformModule) so the snake_case migration applies.
            .UseSnakeCaseNamingConvention()
            .Options);
}
