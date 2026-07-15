using YC.BuildingBlocks.Application.Messaging;

namespace TicketMaster.Modules.Platform.Contracts;

public sealed record PingIntegrationEvent(string Note) : IntegrationEvent;
