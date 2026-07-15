using FluentAssertions;
using YC.BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class SnakeCaseTests
{
    [Theory]
    [InlineData("OrderId", "order_id")]
    [InlineData("userId", "user_id")]
    [InlineData("HTTPStatusCode", "http_status_code")]
    [InlineData("IOError", "io_error")]
    [InlineData("Name", "name")]
    [InlineData("trace_id", "trace_id")]        // already snake → unchanged
    [InlineData("service.name", "service.name")] // dotted semconv → unchanged
    [InlineData("", "")]
    public void Convert_normalizes_to_snake_case(string input, string expected) =>
        SnakeCase.Convert(input).Should().Be(expected);

    [Fact]
    public void Convert_is_idempotent()
    {
        var once = SnakeCase.Convert("OrderId");
        SnakeCase.Convert(once).Should().Be("order_id");
    }
}
