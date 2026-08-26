using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Persistence.Outbox;
using TicketMaster.Modules.Events.Contracts;
using TicketMaster.Modules.Events.Domain;
using TicketMaster.Modules.Events.Infrastructure;

namespace TicketMaster.Modules.Events.Application.Commands;

/// <summary>
/// Schedules a new event and, in the same transaction, appends an <see cref="EventScheduledIntegrationEvent"/> to the
/// outbox (published to Kafka by the relay). Domain-factory failures flow back as Result failures (400).
/// </summary>
public static class ScheduleEventCommand
{
    public sealed record Command(string Name, string Venue, DateTimeOffset StartsOn, int Capacity) : IRequest<Result<Response>>;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(Event.MaxNameLength);
            RuleFor(x => x.Venue).NotEmpty().MaximumLength(Event.MaxVenueLength);
            RuleFor(x => x.Capacity).GreaterThan(0);
        }
    }

    public sealed class Handler(
        EventsDbContext db,
        [FromKeyedServices(nameof(EventsDbContext))] IOutboxWriter outbox) : IRequestHandler<Command, Result<Response>>
    {
        public async ValueTask<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            var scheduled = Event.Schedule(request.Name, request.Venue, request.StartsOn, request.Capacity);
            if (!scheduled.IsSuccess)
                return scheduled.Error;

            var ev = scheduled.Value;
            db.Events.Add(ev);
            outbox.Enqueue(
                new EventScheduledIntegrationEvent(ev.Id.Value, ev.Name, ev.Venue, ev.StartsOn, ev.Capacity),
                aggregateType: nameof(Event), aggregateId: ev.Id.Value.ToString());
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new Response(ev.Id.Value));
        }
    }

    public sealed record Response(Guid Id);
}
