using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketMaster.Modules.Iam.Domain;

namespace TicketMaster.Modules.Iam.Infrastructure.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Ignore(u => u.DomainEvents);
        builder.Ignore(u => u.Roles); // role assignments are persisted in the authorization slice
        builder.Property(u => u.DisplayName).HasMaxLength(200);
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
    }
}
