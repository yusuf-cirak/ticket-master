namespace TicketMaster.BuildingBlocks.Application.Abstractions.Locking;

/// <summary>
/// A distributed mutual-exclusion lock. <see cref="AcquireAsync"/> returns a handle to release on dispose, or
/// <c>null</c> when the lock is already held elsewhere (non-blocking try-acquire). Backed by Redis in production.
/// </summary>
public interface IDistributedLock
{
    ValueTask<IAsyncDisposable> AcquireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default);
}
