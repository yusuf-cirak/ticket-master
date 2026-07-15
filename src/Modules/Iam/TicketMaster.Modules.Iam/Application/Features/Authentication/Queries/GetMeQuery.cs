using MediatR;
using YC.BuildingBlocks.Domain.Security;
using YC.Monad;

namespace TicketMaster.Modules.Iam.Application.Features.Authentication.Queries;

/// <summary>Returns the authenticated caller's subject id (the <c>sub</c> claim).</summary>
public static class GetMeQuery
{
    public sealed record Query : IRequest<Result<Response>>;

    public sealed class Handler(ICurrentUser currentUser) : IRequestHandler<Query, Result<Response>>
    {
        public Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(new Response(currentUser.UserId)));
    }

    public sealed record Response(string UserId);
}
