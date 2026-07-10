using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore;
using TicketMaster.Modules.Iam.Domain;

namespace TicketMaster.Modules.Iam.Infrastructure.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd(); // database-generated int identity
        builder.Ignore(r => r.DomainEvents);
        builder.Property(r => r.Name).HasMaxLength(100);
        builder.HasIndex(r => r.Name).IsUnique();
        builder.PrimitiveCollection(r => r.Permissions); // jsonb array of resource:action strings
        builder.HasConcurrencyToken(); // optimistic concurrency via Postgres xmin
    }
}
