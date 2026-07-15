using System.Text.Json;
using FluentAssertions;
using YC.BuildingBlocks.Domain.ValueObjects;
using Vogen;
using Xunit;

namespace TicketMaster.UnitTests.BuildingBlocks;

public class ValueObjectTests
{
    [Fact]
    public void IpAddress_normalizes_and_validates()
    {
        IpAddress.From("  10.0.0.1  ").Value.Should().Be("10.0.0.1");
        IpAddress.From("2001:db8:0:0:0:0:0:1").Value.Should().Be("2001:db8::1"); // canonical IPv6

        var act = () => IpAddress.From("999.1.1.1");
        act.Should().Throw<ValueObjectValidationException>();

        IpAddress.TryFrom("nope", out _).Should().BeFalse();
    }

    [Fact]
    public void PhoneNumber_strips_formatting_and_enforces_e164()
    {
        PhoneNumber.From(" +1 (415) 555-2671 ").Value.Should().Be("+14155552671");

        var noPlus = () => PhoneNumber.From("14155552671");
        noPlus.Should().Throw<ValueObjectValidationException>();

        var leadingZero = () => PhoneNumber.From("+0155552671");
        leadingZero.Should().Throw<ValueObjectValidationException>();
    }

    [Fact]
    public void Value_objects_serialize_as_their_bare_string()
    {
        JsonSerializer.Serialize(IpAddress.From("10.0.0.1")).Should().Be("\"10.0.0.1\"");
        JsonSerializer.Deserialize<PhoneNumber>("\"+14155552671\"").Value.Should().Be("+14155552671");
    }
}
