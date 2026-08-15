using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketMaster.Modules.Events.Domain;

namespace TicketMaster.Modules.Events.Infrastructure.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever(); // id minted by the domain (Version-7 GUID), not the DB
        builder.Property(e => e.Name).HasMaxLength(Event.MaxNameLength);
        builder.Property(e => e.Venue).HasMaxLength(Event.MaxVenueLength);

        // Every read is tenant-filtered (global query filter) and the list orders by starts_on desc. Without this the
        // query seq-scans all tenants' rows + sorts in memory; the composite index serves both the filter and the sort.
        builder.HasIndex(e => new { e.TenantId, e.StartsOn });
    }
}
