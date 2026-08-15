using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Persistence.Inbox;
using BuildingBlocks.Persistence.Outbox;

namespace TicketMaster.Modules.Platform;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        // Table and column names come from the snake_case convention (PlatformModule). Only configure
        // what the convention can't infer: keys, value generation, and the Postgres jsonb type.
        b.Entity<OutboxMessage>(e =>
        {
            e.Property(x => x.Payload).HasColumnType("jsonb");
            // Partial index for the polling claim (WHERE processed_on IS NULL ORDER BY occurred_on): index scan
            // instead of a full seq-scan + sort every poll.
            e.HasIndex(x => x.OccurredOn).HasFilter("processed_on IS NULL");
        });

        b.Entity<InboxMessage>(e =>
        {
            e.HasKey(x => new { x.MessageId, x.Consumer });
        });
    }
}
