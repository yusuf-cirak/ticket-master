using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YC.BuildingBlocks.Domain;

namespace YC.BuildingBlocks.Persistence.EfCore.Interceptors;

public static class SoftDeleteExtensions
{
    /// <summary>Registers the scoped <see cref="SoftDeleteSaveChangesInterceptor"/> (+ <see cref="TimeProvider"/>).</summary>
    public static IServiceCollection AddSoftDelete(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SoftDeleteSaveChangesInterceptor>();
        return services;
    }

    /// <summary>
    /// Adds a global query filter (<c>e =&gt; !e.IsDeleted</c>) to every mapped <see cref="ISoftDeletable"/> entity, so
    /// soft-deleted rows are excluded from normal queries. Call at the end of <c>OnModelCreating</c>. Use
    /// <c>IgnoreQueryFilters()</c> on a query to include them.
    /// </summary>
    public static void ApplySoftDeleteFilters(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var isDeleted = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            var filter = Expression.Lambda(Expression.Not(isDeleted), parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
