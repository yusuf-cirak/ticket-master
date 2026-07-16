using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using YC.BuildingBlocks.Application.Abstractions.Security;
using YC.BuildingBlocks.Application.Behaviors;
using YC.BuildingBlocks.Domain.Security;
using Xunit;
using YC.Monad;

namespace TicketMaster.UnitTests.Application;

public class SensitiveRequestBehaviorTests
{
    private sealed record SensitiveCommand : ISensitiveRequest;

    private static RequestHandlerDelegate<Result<string>> Handler(Action onCalled)
        => () => { onCalled(); return Task.FromResult(Result.Success("ok")); };

    // Note: the behavior is constrained to ISensitiveRequest (where TRequest : ISensitiveRequest), so MediatR never
    // invokes it for a plain request — there is no runtime pass-through path left to test at this level.

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
        where TRequest : notnull, ISensitiveRequest => new(new FakeUser(authenticated), new FakeBlacklist(revoked));

    private sealed class FakeUser(bool authenticated) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public Option<string> UserId => authenticated ? Option<string>.Some("u1") : Option<string>.None();
        public Option<string> TokenId => authenticated ? Option<string>.Some("jti-1") : Option<string>.None();
        public Option<string> UserName => UserId;
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
