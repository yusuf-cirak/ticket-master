namespace BuildingBlocks.Application.Abstractions.Locking;

/// <summary>
/// Marks a request that must run under a distributed lock — the lock pipeline behavior acquires
/// <see cref="LockKey"/> before the handler and releases it after, so two concurrent requests for the same key can't
/// interleave (the loser gets a 409). Use for genuinely-exactly-once operations (e.g. concurrent duplicate commands
/// the idempotency filter can't serialize).
/// </summary>
public interface ILockRequest
{
    string LockKey { get; }

    TimeSpan LockExpiration => TimeSpan.FromSeconds(30);
}
