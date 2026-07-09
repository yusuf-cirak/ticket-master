using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TicketMaster.Modules.Iam.Domain;

namespace TicketMaster.Modules.Iam.Infrastructure;

/// <summary>
/// IAM persistence. <see cref="IdentityUserContext{TUser, TKey}"/> gives the ASP.NET Identity user tables
/// (users/claims/logins/tokens) keyed by the Vogen <see cref="UserId"/> — Identity roles are not used; roles are the
/// domain <see cref="Role"/> aggregate. All tables live in the <c>iam</c> schema, snake_cased from CLR names.
/// </summary>
public sealed class IamDbContext(DbContextOptions<IamDbContext> options)
    : IdentityUserContext<User, UserId>(options)
{
    public const string Schema = "iam";

    public DbSet<Role> Roles => Set<Role>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(entity =>
        {
            entity.Ignore(u => u.DomainEvents);
            entity.Ignore(u => u.Roles); // role assignments are persisted in the authorization slice
            entity.Property(u => u.DisplayName).HasMaxLength(200);
            entity.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Ignore(r => r.DomainEvents);
            entity.Property(r => r.Name).HasMaxLength(100);
            entity.HasIndex(r => r.Name).IsUnique();
            entity.PrimitiveCollection(r => r.Permissions); // jsonb array of resource:action strings
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<UserId>().HaveConversion<UserId.EfCoreValueConverter>();
        configurationBuilder.Properties<RoleId>().HaveConversion<RoleId.EfCoreValueConverter>();
    }
}
