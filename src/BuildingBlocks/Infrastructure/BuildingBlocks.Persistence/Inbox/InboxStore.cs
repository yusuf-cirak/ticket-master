using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Persistence.Inbox;

public sealed class InboxStore(DbContext dbContext) : IInboxStore
{
    // SQLSTATE 23505 = unique_violation (SQL standard; emitted by Postgres et al.).
    // Matched via System.Data.Common.DbException.SqlState so this building block stays
    // provider-agnostic (no Npgsql reference).
    private const string UniqueViolation = "23505";

    public async Task<bool> TryConsumeAsync(
        Guid messageId,
        string consumer,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        // Fast path: already recorded — skip the work and the round-trip insert.
        var already = await dbContext.Set<InboxMessage>()
            .AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, cancellationToken);
        if (already) return false;

        dbContext.Set<InboxMessage>().Add(new InboxMessage
        {
            MessageId = messageId,
            Consumer = consumer,
            ProcessedOn = DateTimeOffset.UtcNow
        });

        await work(cancellationToken);

        try
        {
            // Atomic: inbox marker + whatever state `work` mutated on this DbContext commit together.
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: UniqueViolation })
        {
            // A concurrent delivery recorded the same (messageId, consumer) between our fast-path
            // check and this insert. The unique key did its job — treat as a duplicate, not a failure,
            // so at-least-once delivery does not turn into a poison-message loop.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
