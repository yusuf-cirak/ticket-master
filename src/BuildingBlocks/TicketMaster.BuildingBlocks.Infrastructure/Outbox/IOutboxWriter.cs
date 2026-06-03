using TicketMaster.BuildingBlocks.Infrastructure.Messaging;

namespace TicketMaster.BuildingBlocks.Infrastructure.Outbox;

public interface IOutboxWriter
{
    // Appends an outbox row to the current DbContext change-tracker (NOT saved here —
    // the caller's SaveChanges commits event + state in one transaction).
    OutboxMessage Enqueue(IntegrationEvent @event, string aggregateType, string aggregateId,
        string? correlationId = null, string? causationId = null);
}
