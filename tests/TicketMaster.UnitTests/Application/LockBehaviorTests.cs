using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using YC.BuildingBlocks.Application.Abstractions.Locking;
using YC.BuildingBlocks.Application.Behaviors;
using Xunit;
using YC.Monad;

namespace TicketMaster.UnitTests.Application;

public class LockBehaviorTests
{
    private sealed record LockedCommand(string LockKey) : ILockRequest;
    private sealed record PlainCommand(string LockKey) : ILockRequest;

    private static RequestHandlerDelegate<Result<string>> Handler(Action onCalled)
        => () => { onCalled(); return Task.FromResult(Result.Success("ok")); };

    [Fact]
    public async Task Lock_acquired_runs_handler()
    {
        var called = false;
        var behavior = new LockBehavior<LockedCommand, Result<string>>(new FakeLock(acquired: true));

        var result = await behavior.Handle(new LockedCommand("k"), Handler(() => called = true), CancellationToken.None);

        called.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Lock_not_acquired_returns_conflict_without_running_handler()
    {
        var called = false;
        var behavior = new LockBehavior<LockedCommand, Result<string>>(new FakeLock(acquired: false));

        var result = await behavior.Handle(new LockedCommand("k"), Handler(() => called = true), CancellationToken.None);

        called.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("lock.conflict");
        result.Error.Status.Should().Be(409);
    }

    private sealed class FakeLock(bool acquired) : IDistributedLock
    {
        public ValueTask<IAsyncDisposable> AcquireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IAsyncDisposable>(acquired ? new NoopHandle() : null);

        private sealed class NoopHandle : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
