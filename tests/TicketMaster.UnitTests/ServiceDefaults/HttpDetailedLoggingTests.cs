using BuildingBlocks.Observability;
using FluentAssertions;
using BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class HttpDetailedLoggingTests
{
    [Fact]
    public void Enabled_by_default() =>
        HttpLoggingExtensions.ResolveEnabled(configuredValue: null).Should().BeTrue();

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]   // only an explicit "false" turns it off
    [InlineData("not-a-bool", true)]
    public void Config_can_turn_it_off(string configured, bool expected) =>
        HttpLoggingExtensions.ResolveEnabled(configured).Should().Be(expected);

    [Fact]
    public void Body_limit_is_one_kilobyte() =>
        HttpLoggingExtensions.BodyLogLimit.Should().Be(1024);
}
