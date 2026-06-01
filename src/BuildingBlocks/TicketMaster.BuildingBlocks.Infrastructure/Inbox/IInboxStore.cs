namespace TicketMaster.BuildingBlocks.Infrastructure.Inbox;

public interface IInboxStore
{
    /// <summary>
    /// Idempotently records the (<paramref name="messageId"/>, <paramref name="consumer"/>) pair and
    /// runs <paramref name="work"/> exactly once. The inbox row and any state mutated by
    /// <paramref name="work"/> on the same <see cref="Microsoft.EntityFrameworkCore.DbContext"/> are
    /// committed in a single SaveChanges, so consumption is atomic: either both the inbox marker and
    /// the work's state changes persist, or neither does.
    /// <para>
    /// Returns <c>true</c> if the message was new and was processed; <c>false</c> if it had already
    /// been processed (in which case <paramref name="work"/> is not run). Safe under concurrent
    /// at-least-once delivery — a lost race on the unique key is treated as a duplicate, not an error.
    /// </para>
    /// </summary>
    Task<bool> TryConsumeAsync(
        Guid messageId,
        string consumer,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken);
}
