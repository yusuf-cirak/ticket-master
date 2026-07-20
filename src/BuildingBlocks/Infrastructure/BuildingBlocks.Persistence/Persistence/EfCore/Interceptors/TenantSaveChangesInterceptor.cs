#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using BuildingBlocks.Application.Abstractions.Tenancy;
using BuildingBlocks.Domain.Tenancy;

namespace BuildingBlocks.Persistence.EfCore.Interceptors;

/// <summary>
/// Write-side tenant enforcement. On save it stamps the ambient tenant onto newly-added <see cref="ITenantOwned"/>
/// entities and rejects any added/modified entity whose tenant differs from the current one — so an entity loaded (or
/// forged) under one tenant can never be persisted under another. Refuses to touch a tenant-owned entity when there is
/// no ambient tenant (fail closed). Scoped; add alongside the other module interceptors.
/// </summary>
public sealed class TenantSaveChangesInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enforce(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Enforce(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Enforce(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            if (!tenantContext.TenantId.TryGetValue(out var current))
                throw new InvalidOperationException(
                    $"Cannot persist tenant-owned entity '{entry.Entity.GetType().Name}' without an ambient tenant.");

            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.SetTenantId(current);
            else if (entry.Entity.TenantId != current)
                throw new InvalidOperationException(
                    $"Cross-tenant write blocked: '{entry.Entity.GetType().Name}' belongs to tenant {entry.Entity.TenantId}, " +
                    $"but the current tenant is {current}.");
        }
    }
}
