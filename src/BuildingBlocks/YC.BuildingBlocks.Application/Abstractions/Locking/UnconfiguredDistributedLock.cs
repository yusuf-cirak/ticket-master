namespace YC.BuildingBlocks.Application.Abstractions.Locking;

/// <summary>
/// Fail-loud default so the lock pipeline behavior can always be constructed, yet an <see cref="ILockRequest"/>
/// dispatched without a real (Redis) lock configured throws instead of silently running unlocked. Registered via
/// <c>TryAddSingleton</c>; the Redis infrastructure registration overrides it.
/// </summary>
public sealed class UnconfiguredDistributedLock : IDistributedLock
{
    public ValueTask<IAsyncDisposable> AcquireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "No distributed lock is configured. Register the Redis infrastructure to use ILockRequest.");
}
