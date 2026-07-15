namespace YC.BuildingBlocks.Infrastructure.Messaging;

public abstract record IntegrationEvent
{
    public Guid MessageId { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
