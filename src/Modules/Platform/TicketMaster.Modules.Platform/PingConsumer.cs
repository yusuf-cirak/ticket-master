using MassTransit;
using Microsoft.Extensions.Logging;
using YC.BuildingBlocks.Infrastructure.Inbox;
using TicketMaster.Modules.Platform.Contracts;

namespace TicketMaster.Modules.Platform;

public sealed class PingConsumer(IInboxStore inbox, ILogger<PingConsumer> logger)
    : IConsumer<PingIntegrationEvent>
{
    public const string ConsumerName = "platform.ping";

    public async Task Consume(ConsumeContext<PingIntegrationEvent> context)
    {
        var processed = await inbox.TryConsumeAsync(
            context.Message.MessageId,
            ConsumerName,
            ct =>
            {
                logger.LogInformation("PingIntegrationEvent consumed: {Note} ({MessageId})",
                    context.Message.Note, context.Message.MessageId);
                return Task.CompletedTask;
            },
            context.CancellationToken);

        if (!processed)
            logger.LogInformation("Duplicate {MessageId} ignored.", context.Message.MessageId);
    }
}
