using Mediator;
using Microsoft.EntityFrameworkCore.Diagnostics;
using BuildingBlocks.Domain;

namespace BuildingBlocks.Persistence.EfCore.Interceptors;

/// <summary>
/// Dispatches aggregates' domain events on save so no developer has to remember to publish them by hand.
/// Registered <b>scoped</b> and added to the DbContext from the same scope, so the injected <see cref="IPublisher"/>
/// resolves handlers against the very DbContext being saved — <see cref="IPreDomainEvent"/> handlers therefore run
/// inside the same transaction. <see cref="IPostDomainEvent"/> handlers run after the changes commit.
///
/// Works identically under an HTTP request scope and a message-consumer scope (no <c>HttpContext</c> dependency).
/// </summary>
public sealed class DomainEventDispatchInterceptor(IPublisher publisher) : SaveChangesInterceptor
{
    private readonly List<IPostDomainEvent> _postCommit = [];

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null)
            return await base.SavingChangesAsync(eventData, result, cancellationToken);

        var aggregates = context.ChangeTracker.Entries<IAggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToArray();

        var events = aggregates.SelectMany(a => a.DomainEvents).ToArray();
        foreach (var aggregate in aggregates)
            aggregate.ClearDomainEvents();

        // Pre-commit: same transaction, before rows are written.
        foreach (var pre in events.OfType<IPreDomainEvent>())
            await publisher.Publish(pre, cancellationToken);

        // Post-commit: stash now, publish once the changes are durable (SavedChangesAsync).
        _postCommit.AddRange(events.OfType<IPostDomainEvent>());

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (_postCommit.Count > 0)
        {
            var toPublish = _postCommit.ToArray();
            _postCommit.Clear();
            foreach (var post in toPublish)
                await publisher.Publish(post, cancellationToken);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    // A save that rolls back must not leave post-commit events queued for the next save on this scoped instance.
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _postCommit.Clear();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _postCommit.Clear();
        return Task.CompletedTask;
    }
}
