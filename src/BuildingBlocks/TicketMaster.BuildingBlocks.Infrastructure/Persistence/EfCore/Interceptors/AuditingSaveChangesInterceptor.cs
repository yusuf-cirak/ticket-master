using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TicketMaster.BuildingBlocks.Domain.Auditing;
using TicketMaster.BuildingBlocks.Domain.Security;

namespace TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore.Interceptors;

/// <summary>
/// Stamps <see cref="IAuditable"/> entities with create/modify actor + UTC timestamp on save, from the scoped
/// <see cref="ICurrentUser"/> and an injectable <see cref="TimeProvider"/> (testable clock). Scoped; add to a
/// module's DbContext via the service-provider overload alongside the domain-event interceptor.
/// </summary>
public sealed class AuditingSaveChangesInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
            return;

        var now = clock.GetUtcNow();
        var actor = currentUser.UserId ?? "system";

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedBy = actor;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedAtUtc = now;
                    entry.Entity.ModifiedBy = actor;
                    break;
            }
        }
    }
}
