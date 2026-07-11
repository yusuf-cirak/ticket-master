using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore;
using TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore.Interceptors;
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

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IamDbContext).Assembly); // IEntityTypeConfiguration<>

        modelBuilder.ApplySoftDeleteFilters(); // global !IsDeleted filter for ISoftDeletable entities
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Register each Vogen value object's generated (reflection-free) EF converter; the scan is a one-time startup cost.
        configurationBuilder.RegisterVogenEfCoreConverters(typeof(IamDbContext).Assembly);
        // Domain string columns are never null — null ⇄ empty string on both read and write.
        configurationBuilder.Properties<string>().HaveConversion<NullToEmptyStringConverter>();
    }
}
