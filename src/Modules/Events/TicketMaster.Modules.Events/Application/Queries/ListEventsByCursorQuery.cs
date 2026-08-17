using Mediator;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Persistence.EfCore;
using TicketMaster.Modules.Events.Domain;
using TicketMaster.Modules.Events.Infrastructure;

namespace TicketMaster.Modules.Events.Application.Queries;

/// <summary>
/// RFC 9865 cursor-paginated list of scheduled events, ordered by <c>Id</c> — a UUIDv7, so it is inherently
/// time-ordered. Unlike <see cref="ListEventsQuery"/> (offset, ordered by <c>StartsOn</c>, always a COUNT), this
/// issues no COUNT and needs no extra index: a boundary range scan on the existing primary-key index does the whole
/// job. See <see cref="CursorRequest"/>/<see cref="UuidV7Cursor"/> for how the opaque cursor decodes into that boundary.
/// </summary>
public static class ListEventsByCursorQuery
{
    public sealed record Query() : IRequest<CursorResult<ListEventsQuery.Item>>;

    public sealed class Handler(EventsDbContext db) : IRequestHandler<Query, CursorResult<ListEventsQuery.Item>>
    {
        public async ValueTask<CursorResult<ListEventsQuery.Item>> Handle(Query request, CancellationToken cancellationToken)
        {
            var (count, boundary, forward) = CursorRequest.FromQuery();
            var boundaryId = boundary is { } b ? Guid.Parse(b) : (Guid?)null;

            IQueryable<Event> events = db.Events;
            if (boundaryId is { } bid)
                events = forward
                    ? events.Where(e => e.Id.CompareTo(EventId.From(bid)) > 0)
                    : events.Where(e => e.Id.CompareTo(EventId.From(bid)) < 0);

            var ordered = forward ? events.OrderBy(e => e.Id) : events.OrderByDescending(e => e.Id);

            return await ordered
                .Select(e => new ListEventsQuery.Item(e.Id, e.Name, e.Venue, e.StartsOn, e.Capacity, e.IsLargeVenue))
                .ToCursorResultAsync(boundaryId, forward, count, item => item.Id.Value, cancellationToken);
        }
    }
}
