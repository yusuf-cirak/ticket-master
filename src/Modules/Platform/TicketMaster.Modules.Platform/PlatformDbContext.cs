using Microsoft.EntityFrameworkCore;
using YC.BuildingBlocks.Persistence.Inbox;
using YC.BuildingBlocks.Persistence.Outbox;

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
        });

        b.Entity<InboxMessage>(e =>
        {
            e.HasKey(x => new { x.MessageId, x.Consumer });
        });
    }
}
