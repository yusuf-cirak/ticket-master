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
    }
}
