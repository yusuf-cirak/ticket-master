using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using BuildingBlocks.Domain;
using BuildingBlocks.Domain.Security;

namespace BuildingBlocks.Persistence.EfCore.Interceptors;

/// <summary>
/// Turns a hard delete of an <see cref="ISoftDeletable"/> entity into a soft delete: the entry is rewritten from
/// Deleted to Modified, <see cref="ISoftDeletable.IsDeleted"/> is set, and who/when is stamped from
/// <see cref="ICurrentUser"/> + <see cref="TimeProvider"/>. Scoped; add to a module's DbContext alongside the other
/// SaveChanges interceptors.
/// </summary>
public sealed class SoftDeleteSaveChangesInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext context)
    {
        if (context is null)
            return;

        var now = clock.GetUtcNow();
        var actor = currentUser.UserId.Match(() => "system", id => id);

        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
                continue;

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAtUtc = now;
            entry.Entity.DeletedBy = actor;
        }
    }
}
