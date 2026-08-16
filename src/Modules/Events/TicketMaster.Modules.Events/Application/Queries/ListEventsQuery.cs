using Mediator;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Persistence.EfCore;
using TicketMaster.Modules.Events.Domain;
using TicketMaster.Modules.Events.Infrastructure;

namespace TicketMaster.Modules.Events.Application.Queries;

/// <summary>Paged list of scheduled events, newest start first. Streams the page straight from EF via the shared
/// <see cref="PaginationExtensions.ToPagedResultAsync"/> helper.</summary>
public static class ListEventsQuery
{
    public sealed record Query() : IRequest<PagedResult<Item>>;

    // Id kept as EventId so EF selects the converted column directly (projecting .Value doesn't translate); the Vogen
    // System.Text.Json converter serializes it back to a plain GUID on the wire. IsLargeVenue is a [Projectable] member
    // on Event — UseProjectables() rewrites it to SQL, so the flag is computed server-side, not client-side.
    public sealed record Item(EventId Id, string Name, string Venue, DateTimeOffset StartsOn, int Capacity, bool IsLargeVenue);

    public sealed class Handler(EventsDbContext db) : IRequestHandler<Query, PagedResult<Item>>
    {
        public ValueTask<PagedResult<Item>> Handle(Query request, CancellationToken cancellationToken) =>
            new(db.Events
                .OrderByDescending(e => e.StartsOn)
                .Select(e => new Item(e.Id, e.Name, e.Venue, e.StartsOn, e.Capacity, e.IsLargeVenue))
                .ToPagedResultAsync(cancellationToken));
    }
}
