using BuildingBlocks.Application.Messaging;

namespace TicketMaster.Modules.Events.Contracts;

/// <summary>Published (via the outbox) when a new event is scheduled — other modules consume it off Kafka.</summary>
public sealed record EventScheduledIntegrationEvent(Guid EventId, string Name, string Venue, DateTimeOffset StartsOn, int Capacity)
    : IntegrationEvent;
