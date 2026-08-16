using Mediator;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Persistence.EfCore;
using TicketMaster.Modules.Events.Domain;
using TicketMaster.Modules.Events.Infrastructure;
using YC.Monad;

namespace TicketMaster.Modules.Events.Application.Queries;

/// <summary>
/// RFC 9865 cursor-paginated list of scheduled events, ordered by <c>Id</c> — a UUIDv7, so it is inherently
/// time-ordered. Unlike <see cref="ListEventsQuery"/> (offset, ordered by <c>StartsOn</c>, always a COUNT), this
/// issues no COUNT and needs no extra index: a boundary range scan on the existing primary-key index does the whole
/// job. See <see cref="CursorRequest"/>/<see cref="UuidV7Cursor"/> for how the opaque cursor decodes into that boundary.
/// </summary>
public static class ListEventsByCursorQuery
{
    public sealed record Query() : IRequest<Result<CursorResult<ListEventsQuery.Item>>>;

    public sealed class Handler(EventsDbContext db) : IRequestHandler<Query, Result<CursorResult<ListEventsQuery.Item>>>
    {
        public async ValueTask<Result<CursorResult<ListEventsQuery.Item>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var parsed = CursorRequest.Create();
            if (!parsed.IsSuccess)
                return parsed.Error;

            var (count, boundary, forward) = parsed.Value;

            IQueryable<Event> events = db.Events;
            if (boundary is { } b)
                events = forward
                    ? events.Where(e => e.Id.CompareTo(EventId.From(b)) > 0)
                    : events.Where(e => e.Id.CompareTo(EventId.From(b)) < 0);

            var ordered = forward ? events.OrderBy(e => e.Id) : events.OrderByDescending(e => e.Id);

            var page = await ordered
                .Select(e => new ListEventsQuery.Item(e.Id, e.Name, e.Venue, e.StartsOn, e.Capacity, e.IsLargeVenue))
                .ToCursorResultAsync(boundary, forward, count, item => item.Id.Value, cancellationToken);

            return Result.Success(page);
        }
    }
}
