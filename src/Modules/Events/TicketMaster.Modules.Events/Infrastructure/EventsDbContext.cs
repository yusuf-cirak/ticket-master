using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Persistence.EfCore;
using BuildingBlocks.Persistence.Outbox;
using TicketMaster.Modules.Events.Domain;

namespace TicketMaster.Modules.Events.Infrastructure;

/// <summary>Events persistence — one <c>events</c> schema, snake_cased from CLR names, Vogen ids converted reflection-free.
/// Carries its own outbox table so a scheduled event and its integration event commit in one transaction.</summary>
public sealed class EventsDbContext(DbContextOptions<EventsDbContext> options) : DbContext(options)
{
    public const string Schema = "events";

    public DbSet<Event> Events => Set<Event>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EventsDbContext).Assembly);

        // Only what the snake_case convention can't infer: the Postgres jsonb payload column.
        modelBuilder.Entity<OutboxMessage>(e => e.Property(x => x.Payload).HasColumnType("jsonb"));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyAppConventions(GetType().Assembly);
}
