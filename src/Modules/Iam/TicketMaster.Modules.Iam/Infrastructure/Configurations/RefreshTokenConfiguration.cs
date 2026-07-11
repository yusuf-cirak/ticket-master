using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketMaster.Modules.Iam.Domain;

namespace TicketMaster.Modules.Iam.Infrastructure.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever(); // app-assigned Guid v7

        // SHA-256 → Base64 is 44 chars; the lookup path (/refresh, /logout) hits this unique index.
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.CreatedByIp).HasMaxLength(64); // fits IPv6 + optional scope id
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);

        builder.HasIndex(t => t.UserId); // UserId persists via the Vogen converter convention
    }
}
