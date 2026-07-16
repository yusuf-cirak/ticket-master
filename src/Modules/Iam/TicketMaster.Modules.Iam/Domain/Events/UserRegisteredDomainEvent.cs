using BuildingBlocks.Domain;
using BuildingBlocks.Domain.ValueObjects;

namespace TicketMaster.Modules.Iam.Domain.Events;

/// <summary>Raised when a user is registered. Post-commit (notify/welcome flows run once the user is durable).</summary>
public sealed record UserRegisteredDomainEvent(UserId UserId, Email Email) : DomainEvent, IPostDomainEvent;
