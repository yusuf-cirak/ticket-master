using BuildingBlocks.Domain;
using BuildingBlocks.Domain.Errors;
using YC.Monad;

namespace TicketMaster.Modules.Events.Domain;

/// <summary>
/// A schedulable event (a show/match/concert) with a venue, start time, and seating capacity. Created through the
/// <see cref="Schedule"/> factory so every instance is valid; EF rehydrates via the private parameterless ctor.
/// </summary>
public sealed class Event : AggregateRoot<EventId>
{
    public const int MaxNameLength = 200;
    public const int MaxVenueLength = 200;

    public string Name { get; private set; } = string.Empty;
    public string Venue { get; private set; } = string.Empty;
    public DateTimeOffset StartsOn { get; private set; }
    public int Capacity { get; private set; }

    private Event() { } // EF

    private Event(EventId id, string name, string venue, DateTimeOffset startsOn, int capacity) : base(id)
    {
        Name = name;
        Venue = venue;
        StartsOn = startsOn;
        Capacity = capacity;
    }

    public static Result<Event> Schedule(string name, string venue, DateTimeOffset startsOn, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Create("event.name_required", "Event name is required.", 400);
        if (name.Length > MaxNameLength)
            return Error.Create("event.name_too_long", $"Event name must be at most {MaxNameLength} characters.", 400);
        if (string.IsNullOrWhiteSpace(venue))
            return Error.Create("event.venue_required", "Venue is required.", 400);
        if (venue.Length > MaxVenueLength)
            return Error.Create("event.venue_too_long", $"Venue must be at most {MaxVenueLength} characters.", 400);
        if (capacity < 1)
            return Error.Create("event.capacity_invalid", "Capacity must be a positive number.", 400);

        return new Event(EventId.New(), name.Trim(), venue.Trim(), startsOn, capacity);
    }
}
