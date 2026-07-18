using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Web;
using TicketMaster.Modules.Events.Application.Commands;
using TicketMaster.Modules.Events.Application.Queries;

namespace TicketMaster.Modules.Events.Host;

/// <summary>
/// The Events HTTP surface — a thin translation layer: bind, send to MediatR, map the Result. Shows the reusable
/// building blocks in action: idempotent create (<c>Idempotency-Key</c> replay), streamed paged list, RFC9457 errors.
/// </summary>
public static class EventEndpoints
{
    public static void MapEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Mounted under the host's "/api" group, so this yields /api/events.
        var group = endpoints.MapGroup("/events").WithTags("Events").AllowAnonymous();

        // A retry carrying the same Idempotency-Key replays the first response instead of scheduling twice.
        group.MapPost("/", async (ScheduleEventCommand.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/events/{result.Value.Id}", result.Value)
                : result.ToApiResult();
        }).RequireIdempotency();

        group.MapGet("/", async (int? page, int? pageSize, ISender sender) =>
            Results.Ok(await sender.Send(
                new ListEventsQuery.Query(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize)))));

        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
            (await sender.Send(new GetEventQuery.Query(id))).ToApiResult());
    }
}
