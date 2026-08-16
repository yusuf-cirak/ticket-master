using Mediator;
using BuildingBlocks.Application.Abstractions.Security;
using YC.Monad;

namespace TicketMaster.Modules.Iam.Application.Features.Authentication.Queries;

/// <summary>Returns the authenticated caller's subject id (the <c>sub</c> claim).</summary>
public static class GetMeQuery
{
    public sealed record Query : IRequest<Result<Response>>;

    public sealed class Handler(ICurrentUser currentUser) : IRequestHandler<Query, Result<Response>>
    {
        public ValueTask<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
            // /me is behind RequireAuthenticatedUser, so the subject is always present; fail loud if that invariant breaks.
            => new(Result.Success(new Response(currentUser.UserId.GetValueOrFail())));
    }

    public sealed record Response(string UserId);
}
