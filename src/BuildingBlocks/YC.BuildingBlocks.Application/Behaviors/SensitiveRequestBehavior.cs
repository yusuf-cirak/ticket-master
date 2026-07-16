using MediatR;
using YC.BuildingBlocks.Application.Abstractions.Security;
using YC.BuildingBlocks.Domain.Security;
using YC.Monad;

namespace YC.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Re-verifies the caller for a request marked <see cref="ISensitiveRequest"/>: it must be authenticated and its
/// token must not have been revoked since issuance. Failures are returned as a 401 through the Result channel (no
/// exception). Non-sensitive requests pass straight through.
/// </summary>
public sealed class SensitiveRequestBehavior<TRequest, TResponse>(ICurrentUser currentUser, ITokenBlacklist blacklist)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ISensitiveRequest
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return ResultFailureFactory<TResponse>.Create(
                Error.Create("auth.required", "Authentication is required for this operation.", 401));

        if (!string.IsNullOrEmpty(currentUser.TokenId) && await blacklist.IsRevokedAsync(currentUser.TokenId, cancellationToken))
            return ResultFailureFactory<TResponse>.Create(
                Error.Create("auth.revoked", "Your session is no longer valid. Please sign in again.", 401));

        return await next();
    }
}
