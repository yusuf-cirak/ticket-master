using Mediator;
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Domain.Errors;
using TicketMaster.Modules.Events.Domain;
using TicketMaster.Modules.Events.Infrastructure;
using YC.Monad;

namespace TicketMaster.Modules.Events.Application.Queries;

/// <summary>Fetches one event by id; a missing id returns a 404 through the Result channel (no exception).</summary>
public static class GetEventQuery
{
    public sealed record Query(Guid Id) : IRequest<Result<Detail>>;

    public sealed record Detail(EventId Id, string Name, string Venue, DateTimeOffset StartsOn, int Capacity);

    public sealed class Handler(EventsDbContext db) : IRequestHandler<Query, Result<Detail>>
    {
        public async ValueTask<Result<Detail>> Handle(Query request, CancellationToken cancellationToken)
        {
            var id = EventId.From(request.Id);
            var detail = await db.Events
                .Where(e => e.Id == id)
                .Select(e => new Detail(e.Id, e.Name, e.Venue, e.StartsOn, e.Capacity))
                .FirstOrDefaultAsync(cancellationToken);

            return detail is null
                ? Error.Create("event.not_found", "Event not found.", 404)
                : Result.Success(detail);
        }
    }
}
