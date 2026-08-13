using FluentAssertions;
using TicketMaster.Modules.Events.Domain;
using TicketMaster.Modules.Events.Domain.Events;
using Xunit;

namespace TicketMaster.UnitTests.Modules.Events;

public class EventTests
{
    private static readonly DateTimeOffset Soon = DateTimeOffset.UnixEpoch.AddYears(60);

    [Fact]
    public void Schedule_with_valid_input_succeeds_and_trims()
    {
        var result = Event.Schedule("  Final  ", "  Arena  ", Soon, 100);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Final");
        result.Value.Venue.Should().Be("Arena");
        result.Value.Capacity.Should().Be(100);
        result.Value.Id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Schedule_raises_a_single_EventScheduled_domain_event()
    {
        var ev = Event.Schedule("Final", "Arena", Soon, 100).Value;

        var raised = ev.DomainEvents.OfType<EventScheduledDomainEvent>().Should().ContainSingle().Subject;
        raised.Id.Should().Be(ev.Id);
        raised.Name.Should().Be("Final");
        raised.StartsOn.Should().Be(Soon);
        raised.Capacity.Should().Be(100);
    }

    [Theory]
    [InlineData("", "Arena", 100, "event.name_required")]
    [InlineData("Final", "", 100, "event.venue_required")]
    [InlineData("Final", "Arena", 0, "event.capacity_invalid")]
    [InlineData("Final", "Arena", -5, "event.capacity_invalid")]
    public void Schedule_with_invalid_input_returns_the_matching_error(string name, string venue, int capacity, string code)
    {
        Event.Schedule(name, venue, Soon, capacity).Error.Code.Should().Be(code);
    }

    [Fact]
    public void Schedule_rejects_an_over_long_name()
    {
        Event.Schedule(new string('x', Event.MaxNameLength + 1), "Arena", Soon, 10)
            .Error.Code.Should().Be("event.name_too_long");
    }
}
