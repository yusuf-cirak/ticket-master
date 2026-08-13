using BuildingBlocks.Domain;

namespace TicketMaster.Modules.Events.Domain.Events;

/// <summary>Raised when an event is scheduled. Post-commit (integration/notify flows run once the event is durable).</summary>
public sealed record EventScheduledDomainEvent(EventId Id, string Name, DateTimeOffset StartsOn, int Capacity)
    : DomainEvent, IPostDomainEvent;
