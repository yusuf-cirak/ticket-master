using MediatR;

namespace BuildingBlocks.Domain;

/// <summary>
/// A domain event. Extends MediatR's <see cref="INotification"/> so it can be published through the mediator
/// to any number of in-process handlers. Prefer the timing-specific <see cref="IPreDomainEvent"/> /
/// <see cref="IPostDomainEvent"/> markers so the dispatch interceptor knows when to publish it.
/// </summary>
public interface IDomainEvent : INotification
{
    Guid EventId { get; }
    DateTimeOffset OccurredOn { get; }
}

/// <summary>
/// Dispatched by the SaveChanges interceptor BEFORE the changes are written, in the SAME transaction/scope —
/// use for effects that must be atomic with the aggregate change (e.g. writing an outbox row, enforcing an
/// invariant across aggregates). A handler here shares the originating DbContext.
/// </summary>
public interface IPreDomainEvent : IDomainEvent;

/// <summary>
/// Dispatched AFTER the changes commit — use for effects that must only run once the state is durable
/// (e.g. sending a notification, kicking off a follow-up workflow). Not part of the write transaction.
/// </summary>
public interface IPostDomainEvent : IDomainEvent;

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
