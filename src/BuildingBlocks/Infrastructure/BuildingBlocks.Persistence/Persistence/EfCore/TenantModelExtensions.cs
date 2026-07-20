using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Domain.Tenancy;

namespace BuildingBlocks.Persistence.EfCore;

/// <summary>A DbContext that carries the current tenant so its model can filter tenant-owned entities against it.</summary>
public interface ITenantScopedContext
{
    /// <summary>The ambient tenant, read live per query (EF re-evaluates it — so an override takes effect immediately).</summary>
    Guid CurrentTenantId { get; }
}

public static class TenantModelExtensions
{
    /// <summary>
    /// Adds a global query filter <c>e =&gt; e.TenantId == context.CurrentTenantId</c> to every <see cref="ITenantOwned"/>
    /// entity, so <b>reads are tenant-scoped automatically</b> — a developer can't forget to filter, and cross-tenant
    /// rows never surface. A deliberate cross-tenant/admin read opts out per query with <c>IgnoreQueryFilters()</c>.
    /// Call at the end of <c>OnModelCreating</c> with the context instance.
    /// </summary>
    public static void ApplyTenantFilters(this ModelBuilder modelBuilder, ITenantScopedContext context)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
                continue;

            var entity = Expression.Parameter(entityType.ClrType, "e");
            // e.TenantId == context.CurrentTenantId  — the context is captured, its property re-read on every query.
            var body = Expression.Equal(
                Expression.Property(entity, nameof(ITenantOwned.TenantId)),
                Expression.Property(Expression.Constant(context), nameof(ITenantScopedContext.CurrentTenantId)));

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, entity));
        }
    }
}
