using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using YC.BuildingBlocks.Infrastructure.Messaging;
using YC.BuildingBlocks.Infrastructure.Outbox;
using Xunit;

namespace TicketMaster.UnitTests.BuildingBlocks;

public sealed record ThingHappened(string Name) : IntegrationEvent;

public sealed class OutboxTestContext(DbContextOptions<OutboxTestContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b) =>
        b.Entity<OutboxMessage>().HasKey(x => x.Id);
}

public class OutboxWriterTests
{
    [Fact]
    public async Task Enqueue_writes_serialized_row_on_save()
    {
        var options = new DbContextOptionsBuilder<OutboxTestContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var ctx = new OutboxTestContext(options);

        var writer = new OutboxWriter(ctx);
        var evt = new ThingHappened("abc");
        writer.Enqueue(evt, aggregateType: "platform", aggregateId: "agg-1", correlationId: "corr-1");
        await ctx.SaveChangesAsync();

        var row = await ctx.Set<OutboxMessage>().SingleAsync();
        row.Id.Should().Be(evt.MessageId);
        row.AggregateType.Should().Be("platform");
        row.AggregateId.Should().Be("agg-1");
        row.EventType.Should().Be(nameof(ThingHappened));
        row.CorrelationId.Should().Be("corr-1");
        row.Payload.Should().Contain("abc");
    }

    [Fact]
    public async Task Enqueue_falls_back_to_ambient_correlation_id_from_baggage()
    {
        var options = new DbContextOptionsBuilder<OutboxTestContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var ctx = new OutboxTestContext(options);

        using var source = new System.Diagnostics.ActivitySource("outbox-test");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _)
                => System.Diagnostics.ActivitySamplingResult.AllData
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("op")!;
        activity.SetBaggage("correlation_id", "corr-from-baggage");

        var writer = new OutboxWriter(ctx);
        writer.Enqueue(new ThingHappened("abc"), aggregateType: "platform", aggregateId: "agg-1");
        await ctx.SaveChangesAsync();

        var row = await ctx.Set<OutboxMessage>().SingleAsync();
        // The durable correlation id (not the trace id) crosses the outbox boundary; trace continuity
        // travels separately in TraceParent.
        row.CorrelationId.Should().Be("corr-from-baggage");
        row.TraceParent.Should().Be(activity.Id);
    }
}
