namespace YC.BuildingBlocks.Infrastructure.Inbox;

public sealed class InboxMessage
{
    public Guid MessageId { get; init; }     // dedup key (= producer MessageId)
    public string Consumer { get; init; } = default!;
    public DateTimeOffset ProcessedOn { get; init; }
}
