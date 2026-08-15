using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Application.Abstractions.Tenancy;
using BuildingBlocks.Persistence.EfCore;
using BuildingBlocks.Persistence.Outbox;
using TicketMaster.Modules.Events.Domain;

namespace TicketMaster.Modules.Events.Infrastructure;

/// <summary>Events persistence — one <c>events</c> schema, snake_cased from CLR names, Vogen ids converted reflection-free.
/// Carries its own outbox table so a scheduled event and its integration event commit in one transaction. Tenant-scoped:
/// every read is filtered to the current tenant automatically.</summary>
public sealed class EventsDbContext(DbContextOptions<EventsDbContext> options, ITenantContext tenantContext)
    : DbContext(options), ITenantScopedContext
{
    public const string Schema = "events";

    public DbSet<Event> Events => Set<Event>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public Guid CurrentTenantId => tenantContext.TenantId.GetValueOrDefault();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EventsDbContext).Assembly);

        // Only what the snake_case convention can't infer: the Postgres jsonb payload column + the polling-claim index.
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.Property(x => x.Payload).HasColumnType("jsonb");
            // The polling relay claims WHERE processed_on IS NULL ORDER BY occurred_on. A partial index on the pending
            // rows keeps it tiny and turns the per-poll seq-scan + sort into an index scan.
            e.HasIndex(x => x.OccurredOn).HasFilter("processed_on IS NULL");
        });

        // Global filter: every query on a tenant-owned entity (Event) is scoped to CurrentTenantId automatically.
        modelBuilder.ApplyTenantFilters(this);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyAppConventions(GetType().Assembly);
}
