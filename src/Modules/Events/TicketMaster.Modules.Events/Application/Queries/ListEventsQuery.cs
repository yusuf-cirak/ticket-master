using MediatR;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Persistence.EfCore;
using TicketMaster.Modules.Events.Domain;
using TicketMaster.Modules.Events.Infrastructure;

namespace TicketMaster.Modules.Events.Application.Queries;

/// <summary>Paged list of scheduled events, newest start first. Streams the page straight from EF via the shared
/// <see cref="PaginationExtensions.ToPagedResultAsync"/> helper.</summary>
public static class ListEventsQuery
{
    public sealed record Query(PageRequest Page) : IRequest<PagedResult<Item>>;

    // Id kept as EventId so EF selects the converted column directly (projecting .Value doesn't translate); the Vogen
    // System.Text.Json converter serializes it back to a plain GUID on the wire.
    public sealed record Item(EventId Id, string Name, string Venue, DateTimeOffset StartsOn, int Capacity);

    public sealed class Handler(EventsDbContext db) : IRequestHandler<Query, PagedResult<Item>>
    {
        public Task<PagedResult<Item>> Handle(Query request, CancellationToken cancellationToken) =>
            db.Events
                .OrderByDescending(e => e.StartsOn)
                .Select(e => new Item(e.Id, e.Name, e.Venue, e.StartsOn, e.Capacity))
                .ToPagedResultAsync(request.Page, cancellationToken);
    }
}
