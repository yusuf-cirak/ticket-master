using TicketMaster.BuildingBlocks.Infrastructure.Messaging;

namespace TicketMaster.Modules.Platform.Contracts;

public sealed record PingIntegrationEvent(string Note) : IntegrationEvent;
