using Mediator;
using BuildingBlocks.Application.Abstractions.Locking;

namespace BuildingBlocks.Application.Behaviors;

/// <summary>
/// Runs a request under a distributed lock when it implements <see cref="ILockRequest"/>. If the lock can't be
/// acquired (another request holds it), the handler is not run and a failed <c>Result</c> (409) is returned through
/// the Result channel — no exception. Requests without the marker pass straight through.
/// </summary>
public sealed class LockBehavior<TRequest, TResponse>(IDistributedLock distributedLock)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, ILockRequest, IMessage
{
    public async ValueTask<TResponse> Handle(TRequest request, MessageHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken)
    {
        ILockRequest lockRequest = request;

        await using var handle = await distributedLock.AcquireAsync(lockRequest.LockKey, lockRequest.LockExpiration, cancellationToken);
        if (handle is null)
            return ResultFailureFactory<TResponse>.Create(
                Error.Create("lock.conflict", "The operation is already in progress. Please retry.", 409));

        return await next(request, cancellationToken);
    }
}
