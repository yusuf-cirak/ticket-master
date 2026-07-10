using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using TicketMaster.BuildingBlocks.Application.Abstractions.Security;
using TicketMaster.BuildingBlocks.Application.Behaviors;
using TicketMaster.BuildingBlocks.Domain.Security;
using Xunit;
using YC.Monad;

namespace TicketMaster.UnitTests.Application;

public class SensitiveRequestBehaviorTests
{
    private sealed record SensitiveCommand : ISensitiveRequest;
    private sealed record PlainCommand;

    private static RequestHandlerDelegate<Result<string>> Handler(Action onCalled)
        => () => { onCalled(); return Task.FromResult(Result.Success("ok")); };

    [Fact]
    public async Task Plain_request_passes_through_even_when_anonymous()
    {
        var called = false;
        var behavior = Behavior<PlainCommand>(authenticated: false, revoked: false);

        var result = await behavior.Handle(new PlainCommand(), Handler(() => called = true), CancellationToken.None);

        called.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Sensitive_request_by_anonymous_is_rejected()
    {
        var called = false;
        var behavior = Behavior<SensitiveCommand>(authenticated: false, revoked: false);

        var result = await behavior.Handle(new SensitiveCommand(), Handler(() => called = true), CancellationToken.None);

        called.Should().BeFalse();
        result.Error.Code.Should().Be("auth.required");
        result.Error.Status.Should().Be(401);
    }

    [Fact]
    public async Task Sensitive_request_with_revoked_token_is_rejected()
    {
        var called = false;
        var behavior = Behavior<SensitiveCommand>(authenticated: true, revoked: true);

        var result = await behavior.Handle(new SensitiveCommand(), Handler(() => called = true), CancellationToken.None);

        called.Should().BeFalse();
        result.Error.Code.Should().Be("auth.revoked");
    }

    [Fact]
    public async Task Sensitive_request_with_valid_session_runs()
    {
        var called = false;
        var behavior = Behavior<SensitiveCommand>(authenticated: true, revoked: false);

        var result = await behavior.Handle(new SensitiveCommand(), Handler(() => called = true), CancellationToken.None);

        called.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    private static SensitiveRequestBehavior<TRequest, Result<string>> Behavior<TRequest>(bool authenticated, bool revoked)
        where TRequest : notnull
        => new(new FakeUser(authenticated), new FakeBlacklist(revoked));

    private sealed class FakeUser(bool authenticated) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string UserId => authenticated ? "u1" : string.Empty;
        public string TokenId => authenticated ? "jti-1" : string.Empty;
        public string UserName => UserId;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
        public bool HasPermission(string permission) => false;
    }

    private sealed class FakeBlacklist(bool revoked) : ITokenBlacklist
    {
        public Task RevokeAsync(string tokenId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken = default)
            => Task.FromResult(revoked);
    }
}
